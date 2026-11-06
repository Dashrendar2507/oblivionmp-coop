#!powershell.exe -ExecutionPolicy Bypass -File
# Used by MakeModFolder.ps1 to build Output/mods/<modName>/{manifest.json, client/, server/}.
$modName = "CoopQuestSync"
$clientProject = "CoopQuestSync.Client"
$serverProject = "CoopQuestSync.Server"
$clientBuildFiles = @("CoopQuestSync.Client.dll")
$serverBuildFiles = @("CoopQuestSync.Server.dll")
$manifestFiles = @("manifest.json")
$clientContentFiles = @(
    "CoopQuestSync.esp"
)
$serverContentFiles = @()
$clientDebugBuildFiles = @("CoopQuestSync.Client.pdb")
$serverDebugBuildFiles = @("CoopQuestSync.Server.pdb")
