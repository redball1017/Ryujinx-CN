﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Ryujinx.Horizon.Generators.Kernel
{
    [Generator]
    class SyscallGenerator : ISourceGenerator
    {
        private const string ClassNamespace = "Ryujinx.HLE.HOS.Kernel.SupervisorCall";
        private const string ClassName = "SyscallDispatch";
        private const string A32Suffix = "32";
        private const string A64Suffix = "64";
        private const string ResultVariableName = "result";
        private const string ArgVariablePrefix = "arg";
        private const string ResultCheckHelperName = "LogResultAsTrace";

        private const string TypeSystemBoolean = "System.Boolean";
        private const string TypeSystemInt32 = "System.Int32";
        private const string TypeSystemInt64 = "System.Int64";
        private const string TypeSystemUInt32 = "System.UInt32";
        private const string TypeSystemUInt64 = "System.UInt64";

        private const string NamespaceKernel = "Ryujinx.HLE.HOS.Kernel";
        private const string TypeSvcAttribute = NamespaceKernel + ".SupervisorCall.SvcAttribute";
        private const string TypePointerSizedAttribute = NamespaceKernel + ".SupervisorCall.PointerSizedAttribute";
        private const string TypeKernelResultName = "KernelResult";
        private const string TypeKernelResult = NamespaceKernel + ".Common." + TypeKernelResultName;
        private const string TypeExecutionContext = "IExecutionContext";

        private static readonly string[] _expectedResults = new string[]
        {
            $"{TypeKernelResultName}.Success",
            $"{TypeKernelResultName}.TimedOut",
            $"{TypeKernelResultName}.Cancelled",
            $"{TypeKernelResultName}.PortRemoteClosed",
            $"{TypeKernelResultName}.InvalidState"
        };

        private struct OutParameter
        {
            public readonly string Identifier;
            public readonly bool NeedsSplit;

            public OutParameter(string identifier, bool needsSplit = false)
            {
                Identifier = identifier;
                NeedsSplit = needsSplit;
            }
        }

        private struct RegisterAllocatorA32
        {
            private uint _useSet;
            private int _linearIndex;

            public int AllocateSingle()
            {
                return Allocate();
            }

            public (int, int) AllocatePair()
            {
                _linearIndex += _linearIndex & 1;

                return (Allocate(), Allocate());
            }

            private int Allocate()
            {
                int regIndex;

                if (_linearIndex < 4)
                {
                    regIndex = _linearIndex++;
                }
                else
                {
                    regIndex = -1;

                    for (int i = 0; i < 32; i++)
                    {
                        if ((_useSet & (1 << i)) == 0)
                        {
                            regIndex = i;
                            break;
                        }
                    }

                    Debug.Assert(regIndex != -1);
                }

                _useSet |= 1u << regIndex;

                return regIndex;
            }

            public void AdvanceLinearIndex()
            {
                _linearIndex++;
            }
        }

        private struct SyscallIdAndName : IComparable<SyscallIdAndName>
        {
            public readonly int Id;
            public readonly string Name;

            public SyscallIdAndName(int id, string name)
            {
                Id = id;
                Name = name;
            }

            public int CompareTo(SyscallIdAndName other)
            {
                return Id.CompareTo(other.Id);
            }
        }

        public void Execute(GeneratorExecutionContext context)
        {
            SyscallSyntaxReceiver syntaxReceiver = (SyscallSyntaxReceiver)context.SyntaxReceiver;

            CodeGenerator generator = new CodeGenerator();

            generator.AppendLine("using Ryujinx.Common.Logging;");
            generator.AppendLine("using Ryujinx.Cpu;");
            generator.AppendLine($"using {NamespaceKernel}.Common;");
            generator.AppendLine($"using {NamespaceKernel}.Memory;");
            generator.AppendLine($"using {NamespaceKernel}.Process;");
            generator.AppendLine($"using {NamespaceKernel}.Threading;");
            generator.AppendLine("using System;");
            generator.AppendLine();
            generator.EnterScope($"namespace {ClassNamespace}");
            generator.EnterScope($"static class {ClassName}");

            GenerateResultCheckHelper(generator);
            generator.AppendLine();

            List<SyscallIdAndName> syscalls = new List<SyscallIdAndName>();

            foreach (var method in syntaxReceiver.SvcImplementations)
            {
                GenerateMethod32(generator, context.Compilation, method);
                GenerateMethod64(generator, context.Compilation, method);

                foreach (var attributeList in method.AttributeLists)
                {
                    foreach (var attribute in attributeList.Attributes)
                    {
                        if (GetCanonicalTypeName(context.Compilation, attribute) != TypeSvcAttribute)
                        {
                            continue;
                        }

                        foreach (var attributeArg in attribute.ArgumentList.Arguments)
                        {
                            if (attributeArg.Expression.Kind() == SyntaxKind.NumericLiteralExpression)
                            {
                                LiteralExpressionSyntax numericLiteral = (LiteralExpressionSyntax)attributeArg.Expression;
                                syscalls.Add(new SyscallIdAndName((int)numericLiteral.Token.Value, method.Identifier.Text));
                            }
                        }
                    }
                }
            }

            syscalls.Sort();

            GenerateDispatch(generator, syscalls, A32Suffix);
            generator.AppendLine();
            GenerateDispatch(generator, syscalls, A64Suffix);

            generator.LeaveScope();
            generator.LeaveScope();

            context.AddSource($"{ClassName}.g.cs", generator.ToString());
        }

        private static void GenerateResultCheckHelper(CodeGenerator generator)
        {
            generator.EnterScope($"private static bool {ResultCheckHelperName}({TypeKernelResultName} {ResultVariableName})");

            string[] expectedChecks = new string[_expectedResults.Length];

            for (int i = 0; i < expectedChecks.Length; i++)
            {
                expectedChecks[i] = $"{ResultVariableName} == {_expectedResults[i]}";
            }

            string checks = string.Join(" || ", expectedChecks);

            generator.AppendLine($"return {checks};");
            generator.LeaveScope();
        }

        private static void GenerateMethod32(CodeGenerator generator, Compilation compilation, MethodDeclarationSyntax method)
        {
            generator.EnterScope($"private static void {method.Identifier.Text}{A32Suffix}(Syscall syscall, {TypeExecutionContext} context)");

            string[] args = new string[method.ParameterList.Parameters.Count];
            int index = 0;

            RegisterAllocatorA32 regAlloc = new RegisterAllocatorA32();

            List<OutParameter> outParameters = new List<OutParameter>();
            List<string> logInArgs = new List<string>();
            List<string> logOutArgs = new List<string>();

            foreach (var methodParameter in method.ParameterList.Parameters)
            {
                string name = methodParameter.Identifier.Text;
                string argName = GetPrefixedArgName(name);
                string typeName = methodParameter.Type.ToString();
                string canonicalTypeName = GetCanonicalTypeName(compilation, methodParameter.Type);

                if (methodParameter.Modifiers.Any(SyntaxKind.OutKeyword))
                {
                    bool needsSplit = Is64BitInteger(canonicalTypeName) && !IsPointerSized(compilation, methodParameter);
                    outParameters.Add(new OutParameter(argName, needsSplit));
                    logOutArgs.Add($"{name}: {GetFormattedLogValue(argName, canonicalTypeName)}");

                    argName = $"out {typeName} {argName}";

                    regAlloc.AdvanceLinearIndex();
                }
                else
                {
                    if (Is64BitInteger(canonicalTypeName))
                    {
                        if (IsPointerSized(compilation, methodParameter))
                        {
                            int registerIndex = regAlloc.AllocateSingle();

                            generator.AppendLine($"var {argName} = (uint)context.GetX({registerIndex});");
                        }
                        else
                        {
                            (int registerIndex, int registerIndex2) = regAlloc.AllocatePair();

                            string valueLow = $"(ulong)(uint)context.GetX({registerIndex})";
                            string valueHigh = $"(ulong)(uint)context.GetX({registerIndex2})";
                            string value = $"{valueLow} | ({valueHigh} << 32)";

                            generator.AppendLine($"var {argName} = ({typeName})({value});");
                        }
                    }
                    else
                    {
                        int registerIndex = regAlloc.AllocateSingle();

                        string value = GenerateCastFromUInt64($"context.GetX({registerIndex})", canonicalTypeName, typeName);

                        generator.AppendLine($"var {argName} = {value};");
                    }

                    logInArgs.Add($"{name}: {GetFormattedLogValue(argName, canonicalTypeName)}");
                }

                args[index++] = argName;
            }

            GenerateLogPrintBeforeCall(generator, method.Identifier.Text, logInArgs);

            string returnTypeName = method.ReturnType.ToString();
            string argsList = string.Join(", ", args);
            int returnRegisterIndex = 0;
            string result = null;
            string canonicalReturnTypeName = null;

            if (returnTypeName != "void")
            {
                generator.AppendLine($"var {ResultVariableName} = syscall.{method.Identifier.Text}({argsList});");
                generator.AppendLine($"context.SetX({returnRegisterIndex++}, (uint){ResultVariableName});");

                canonicalReturnTypeName = GetCanonicalTypeName(compilation, method.ReturnType);

                if (Is64BitInteger(canonicalReturnTypeName))
                {
                    generator.AppendLine($"context.SetX({returnRegisterIndex++}, (uint)({ResultVariableName} >> 32));");
                }

                result = GetFormattedLogValue(ResultVariableName, canonicalReturnTypeName);
            }
            else
            {
                generator.AppendLine($"syscall.{method.Identifier.Text}({argsList});");
            }

            foreach (OutParameter outParameter in outParameters)
            {
                generator.AppendLine($"context.SetX({returnRegisterIndex++}, (uint){outParameter.Identifier});");

                if (outParameter.NeedsSplit)
                {
                    generator.AppendLine($"context.SetX({returnRegisterIndex++}, (uint)({outParameter.Identifier} >> 32));");
                }
            }

            while (returnRegisterIndex < 4)
            {
                generator.AppendLine($"context.SetX({returnRegisterIndex++}, 0);");
            }

            GenerateLogPrintAfterCall(generator, method.Identifier.Text, logOutArgs, result, canonicalReturnTypeName);

            generator.LeaveScope();
            generator.AppendLine();
        }

        private static void GenerateMethod64(CodeGenerator generator, Compilation compilation, MethodDeclarationSyntax method)
        {
            generator.EnterScope($"private static void {method.Identifier.Text}{A64Suffix}(Syscall syscall, {TypeExecutionContext} context)");

            string[] args = new string[method.ParameterList.Parameters.Count];
            int registerIndex = 0;
            int index = 0;

            List<OutParameter> outParameters = new List<OutParameter>();
            List<string> logInArgs = new List<string>();
            List<string> logOutArgs = new List<string>();

            foreach (var methodParameter in method.ParameterList.Parameters)
            {
                string name = methodParameter.Identifier.Text;
                string argName = GetPrefixedArgName(name);
                string typeName = methodParameter.Type.ToString();
                string canonicalTypeName = GetCanonicalTypeName(compilation, methodParameter.Type);

                if (methodParameter.Modifiers.Any(SyntaxKind.OutKeyword))
                {
                    outParameters.Add(new OutParameter(argName));
                    logOutArgs.Add($"{name}: {GetFormattedLogValue(argName, canonicalTypeName)}");
                    argName = $"out {typeName} {argName}";
                    registerIndex++;
                }
                else
                {
                    string value = GenerateCastFromUInt64($"context.GetX({registerIndex++})", canonicalTypeName, typeName);
                    generator.AppendLine($"var {argName} = {value};");
                    logInArgs.Add($"{name}: {GetFormattedLogValue(argName, canonicalTypeName)}");
                }

                args[index++] = argName;
            }

            GenerateLogPrintBeforeCall(generator, method.Identifier.Text, logInArgs);

            string argsList = string.Join(", ", args);
            int returnRegisterIndex = 0;
            string result = null;
            string canonicalReturnTypeName = null;

            if (method.ReturnType.ToString() != "void")
            {
                generator.AppendLine($"var {ResultVariableName} = syscall.{method.Identifier.Text}({argsList});");
                generator.AppendLine($"context.SetX({returnRegisterIndex++}, (ulong){ResultVariableName});");
                canonicalReturnTypeName = GetCanonicalTypeName(compilation, method.ReturnType);
                result = GetFormattedLogValue(ResultVariableName, canonicalReturnTypeName);
            }
            else
            {
                generator.AppendLine($"syscall.{method.Identifier.Text}({argsList});");
            }

            foreach (OutParameter outParameter in outParameters)
            {
                generator.AppendLine($"context.SetX({returnRegisterIndex++}, (ulong){outParameter.Identifier});");
            }

            while (returnRegisterIndex < 8)
            {
                generator.AppendLine($"context.SetX({returnRegisterIndex++}, 0);");
            }

            GenerateLogPrintAfterCall(generator, method.Identifier.Text, logOutArgs, result, canonicalReturnTypeName);

            generator.LeaveScope();
            generator.AppendLine();
        }

        private static string GetFormattedLogValue(string value, string canonicalTypeName)
        {
            if (Is32BitInteger(canonicalTypeName))
            {
                return $"0x{{{value}:X8}}";
            }
            else if (Is64BitInteger(canonicalTypeName))
            {
                return $"0x{{{value}:X16}}";
            }

            return $"{{{value}}}";
        }

        private static string GetPrefixedArgName(string name)
        {
            return ArgVariablePrefix + name[0].ToString().ToUpperInvariant() + name.Substring(1);
        }

        private static string GetCanonicalTypeName(Compilation compilation, SyntaxNode syntaxNode)
        {
            TypeInfo typeInfo = compilation.GetSemanticModel(syntaxNode.SyntaxTree).GetTypeInfo(syntaxNode);
            if (typeInfo.Type.ContainingNamespace == null)
            {
                return typeInfo.Type.Name;
            }

            return $"{typeInfo.Type.ContainingNamespace.ToDisplayString()}.{typeInfo.Type.Name}";
        }

        private static void GenerateLogPrintBeforeCall(CodeGenerator generator, string methodName, List<string> argList)
        {
            string log = $"{methodName}({string.Join(", ", argList)})";
            GenerateLogPrint(generator, "Trace", "KernelSvc", log);
        }

        private static void GenerateLogPrintAfterCall(
            CodeGenerator generator,
            string methodName,
            List<string> argList,
            string result,
            string canonicalResultTypeName)
        {
            string log = $"{methodName}({string.Join(", ", argList)})";

            if (result != null)
            {
                log += $" = {result}";
            }

            if (canonicalResultTypeName == TypeKernelResult)
            {
                generator.EnterScope($"if ({ResultCheckHelperName}({ResultVariableName}))");
                GenerateLogPrint(generator, "Trace", "KernelSvc", log);
                generator.LeaveScope();
                generator.EnterScope("else");
                GenerateLogPrint(generator, "Warning", "KernelSvc", log);
                generator.LeaveScope();
            }
            else
            {
                GenerateLogPrint(generator, "Trace", "KernelSvc", log);
            }
        }

        private static void GenerateLogPrint(CodeGenerator generator, string logLevel, string logClass, string log)
        {
            generator.AppendLine($"Logger.{logLevel}?.PrintMsg(LogClass.{logClass}, $\"{log}\");");
        }

        private static void GenerateDispatch(CodeGenerator generator, List<SyscallIdAndName> syscalls, string suffix)
        {
            generator.EnterScope($"public static void Dispatch{suffix}(Syscall syscall, {TypeExecutionContext} context, int id)");
            generator.EnterScope("switch (id)");

            foreach (var syscall in syscalls)
            {
                generator.AppendLine($"case {syscall.Id}:");
                generator.IncreaseIndentation();

                generator.AppendLine($"{syscall.Name}{suffix}(syscall, context);");
                generator.AppendLine("break;");

                generator.DecreaseIndentation();
            }

            generator.AppendLine($"default:");
            generator.IncreaseIndentation();

            generator.AppendLine("throw new NotImplementedException($\"SVC 0x{id:X4} is not implemented.\");");

            generator.DecreaseIndentation();

            generator.LeaveScope();
            generator.LeaveScope();
        }

        private static bool Is32BitInteger(string canonicalTypeName)
        {
            return canonicalTypeName == TypeSystemInt32 || canonicalTypeName == TypeSystemUInt32;
        }

        private static bool Is64BitInteger(string canonicalTypeName)
        {
            return canonicalTypeName == TypeSystemInt64 || canonicalTypeName == TypeSystemUInt64;
        }

        private static string GenerateCastFromUInt64(string value, string canonicalTargetTypeName, string targetTypeName)
        {
            if (canonicalTargetTypeName == TypeSystemBoolean)
            {
                return $"({value} & 1) != 0";
            }

            return $"({targetTypeName}){value}";
        }

        private static bool IsPointerSized(Compilation compilation, ParameterSyntax parameterSyntax)
        {
            foreach (var attributeList in parameterSyntax.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    if (GetCanonicalTypeName(compilation, attribute) == TypePointerSizedAttribute)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyscallSyntaxReceiver());
        }
    }
}
