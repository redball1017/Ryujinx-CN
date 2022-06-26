 @echo off
 echo "正在添加所有文件到git暂存区"
 git add .
 echo "正在提交到本地仓库"
 git commit -m "更新"
 echo "正在从远程获取最新版本并merge到本地"
 git pull Ryujinx-CN main
 echo "正在push到远程服务器github"
 git push -u Ryujinx-CN main
 pause 
 exit