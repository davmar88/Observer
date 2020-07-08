#!/bin/sh
# This is a comment!
echo starting hss        # This is a comment, too!

cd ~
cd githubdownloads/new/openair-cn/scripts
sudo sh -c "exec dos2unix -k -o run_hss"
(exec "./run_hss")


