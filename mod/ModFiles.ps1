#!powershell.exe -ExecutionPolicy Bypass -File
# Used by MakeModFolder.ps1 to build Output/mods/<modName>/{manifest.json, client/, server/}.
$modName = "CoopQuestSync"
$clientProject = "CoopQuestSync.Client"
$serverProject = "CoopQuestSync.Server"
$clientBuildFiles = @("CoopQuestSync.Client.dll", "CoopQuestSync.Common.dll", "web/index.html", "addon/CoopQuestSyncUI/enabled.txt", "addon/CoopQuestSyncUI/Scripts/main.lua")
$serverBuildFiles = @("CoopQuestSync.Server.dll", "CoopQuestSync.Common.dll")
$manifestFiles = @("manifest.json")
$clientContentFiles = @(
    "CoopQuestSync.esp",
    "CoopQuestSyncDialogue.esp"
)
$serverContentFiles = @()
$clientDebugBuildFiles = @("CoopQuestSync.Client.pdb")
$serverDebugBuildFiles = @("CoopQuestSync.Server.pdb")
