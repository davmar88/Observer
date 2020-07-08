#!/bin/sh
# This is a comment!
echo starting mme        # This is a comment, too!

cd ~
cd githubdownloads/new/openair-cn/scripts
sudo sh -c "exec dos2unix -k -o run_mme"
(exec "./run_mme")

