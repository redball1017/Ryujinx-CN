# Ryujinx-CN

这里是Nintendo Switch模拟器[Ryujinx](https://github.com/Ryujinx/Ryujinx)的汉化版，目前只有[我](https://github.com/redball1017)一个人在维护，也欢迎各位会C#的人来帮忙维护!

## 其他下载地址

我自己搭建的网盘:[点我前往](https://mirror.redball101713781397.tk/zh-CN/Ryujinx-CN/)

Gitee下载地址:[点我前往](https://gitee.com/redball1017/Ryujinx-CN/releases)

## 如何构建

### 需要的软件及硬件

- [Git](https://git-scm.com/)(可选)

- [.NET 6.0 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/6.0)(要下载SDK而不是运行时Runtime)

- 终端或者其他的命令行工具

- x86_64架构的系统

  ### 教程

  #### 克隆仓库

  ##### 用Git克隆

  调出终端后运行以下命令来克隆本仓库

  国外推荐:

  `git clone https://github.com/redball1017/Ryujinx-CN.git`

  国内推荐:

  `git clone https://gitee.com/redball1017/Ryujinx-CN.git`

  ##### 下载zip文件来克隆

  github下载地址:[点我下载](https://github.com/redball1017/Ryujinx-CN/archive/refs/heads/main.zip)

  gitee下载地址:[点我下载](https://gitee.com/redball1017/Ryujinx-CN/repository/archive/main.zip)

  下载完成后将压缩包里面的文件解压出来即可

  #### 开始构建

  首先，先进入你刚刚克隆的仓库文件夹(通常名为Ryujinx-CN)

  然后按右键(Windows要按住Shift键)点击"在此窗口打开终端"（或者类似意思的文件）

  然后输入以下命令来构建Ryujinx

  `dotnet build -c Release -o build`

  等它运行之后，Ryujinx-CN文件夹中就有一个名为Build的文件夹，里面就是已经构建好的Ryujinx二进制文件。

