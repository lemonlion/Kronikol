for s in [ "http://x/a" + string (char 92) + "tb?c=d" + string (char 92) + "te"; "redis://host/key" + string (char 92) + "tx"; "kafka://broker/topic?k=a" + string (char 92) + "nb"; "http://x/a~b?q=%date()&r=<U+0041>"; "sql://db/SELECT" + string (char 92) + "t1" ] do
    let u = System.Uri(s)
    printfn "%-40s -> PathAndQuery %s" s u.PathAndQuery
