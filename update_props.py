import sys
import os

xml = """<Project>
  <PropertyGroup>
    <Version>1.0.$([System.DateTime]::UtcNow.ToString("yyMMdd"))</Version>
  </PropertyGroup>
</Project>"""

with open("Directory.Build.props", "w", encoding="utf-8") as f:
    f.write(xml)