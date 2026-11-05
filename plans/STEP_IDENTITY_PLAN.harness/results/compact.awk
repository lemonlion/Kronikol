/^### s[0-9]+ steps/{p=1;print "";next}
/^### s[0-9]+ calls/{p=2;next}
/^### summary/{p=0}
p==1 && !/^stableId|^at Features|^open:|^$|^[0-9]+ calls ·|^next:/{print}
p==2 && !/^step  method|buckets$|^$/{print "      call> " $1 " " $3}
