$ErrorActionPreference = 'Stop'

function Split-RazorFile {
    param([string]$FilePath)
    
    $content = Get-Content -Path $FilePath -Raw
    
    if ($content -match '(?ms)^(.*?)@code\s*\{(.*)\}\s*$') {
        $htmlContent = $matches[1].TrimEnd() + "
"
        $codeContent = $matches[2].Trim()
        
        # We need to find the class name (file name without extension) and namespace
        $fileName = [System.IO.Path]::GetFileNameWithoutExtension($FilePath)
        
        # Determine namespace based on path relative to QuimeraReader.Shared
        $relativePath = (Resolve-Path -Path $FilePath -Relative).Replace('.\QuimeraReader.Clients\QuimeraReader.Shared\', '').Replace('\', '.')
        $namespaceSuffix = $relativePath.Substring(0, $relativePath.LastIndexOf('.')).Replace('.' + $fileName, '')
        
        if ($namespaceSuffix) {
            $namespace = "QuimeraReader.Shared." + $namespaceSuffix
        } else {
            $namespace = "QuimeraReader.Shared"
        }
        
        # Write the .razor.cs file
        $csPath = $FilePath + ".cs"
        
        # Determine usings based on common injects or just use default
        $csContent = "using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace $namespace;

public partial class $fileName
{
"
        
        # Fix indentation of code block
        $lines = $codeContent -split "
|
"
        foreach ($line in $lines) {
            $csContent += "    " + $line + "
"
        }
        
        $csContent += "}
"
        
        Set-Content -Path $csPath -Value $csContent -Encoding UTF8
        Set-Content -Path $FilePath -Value $htmlContent -Encoding UTF8
        Write-Host "Split $fileName successfully"
    } else {
        Write-Host "No @code block found in $FilePath"
    }
}

Get-ChildItem -Path '.\QuimeraReader.Clients\QuimeraReader.Shared\' -Filter '*.razor' -Recurse | ForEach-Object {
    Split-RazorFile -FilePath $_.FullName
}
Get-ChildItem -Path '.\QuimeraReader.Clients\QuimeraReader.Web\' -Filter '*.razor' -Recurse | ForEach-Object { Split-RazorFile -FilePath $_.FullName }
Get-ChildItem -Path '.\QuimeraReader.Clients\QuimeraReader.Mobile\' -Filter '*.razor' -Recurse | ForEach-Object { Split-RazorFile -FilePath $_.FullName }
