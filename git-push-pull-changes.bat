 @echo off
 title ����Ryujinx-CN���ظ��µ�Github��Gitee
 echo ������������ļ���git�ݴ���
 git add .
 echo �����ύ�����زֿ�
 git commit -m "����"
 echo ���ڴ�Զ�̻�ȡ���°汾��merge������
 git pull Ryujinx-CN main
 echo ����push��Զ�̷�����github��Gitee
 git push -u Ryujinx-CN main
git push -u gitee main
 pause 
 exit