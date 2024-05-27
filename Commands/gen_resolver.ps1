# 生成代码
mpc -i "..\Queen.Protocols\Queen.Protocols.csproj" -o "..\Queen.Protocols\Common\MessagePackGenerated.cs" -m

# 添加命名空间
# $filePath = "..\Queen.Protocols\Common\MessagePackGenerated.cs"
# $firstLine = "`nusing System.Collections.Generic;"
# (Get-Content -Path $filePath) | ForEach-Object {
#     $_
#     if ($_.ReadCount -eq 3) { $firstLine }
# } | Set-Content -Path $filePath
