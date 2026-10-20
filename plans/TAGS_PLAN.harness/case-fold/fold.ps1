# .NET's OrdinalIgnoreCase on two characters outside ASCII (PowerShell 7 runs on .NET).
$i = [char]0x0131; $k = [char]0x212A
"dotnet OrdinalIgnoreCase: dotless i == i -> {0}; dotless i == I -> {1}; Kelvin == k -> {2}" -f `
    [string]::Equals("$i", 'i', [StringComparison]::OrdinalIgnoreCase),
    [string]::Equals("$i", 'I', [StringComparison]::OrdinalIgnoreCase),
    [string]::Equals("$k", 'k', [StringComparison]::OrdinalIgnoreCase)
