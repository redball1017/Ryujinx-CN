 @echo off
 echo "������������ļ���git�ݴ���"
 git add .
 echo "�����ύ�����زֿ�"
 git commit -m "����"
 echo "���ڴ�Զ�̻�ȡ���°汾��merge������"
 git pull Ryujinx-CN main
 echo "����push��Զ�̷�����github"
 git push -u Ryujinx-CN main
 pause 
 exit