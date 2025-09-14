[Setup]
AppName=LogFileCollector
AppVersion={#MyAppVersion}
AppVerName=LogFileCollector {#MyAppVersion}
OutputBaseFilename=LogFileCollectorSetup-{#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=Virinco AS
VersionInfoDescription=LogFileCollector Installer
VersionInfoProductName=LogFileCollector
VersionInfoCopyright=Copyright © Virinco AS 2025

[Files]
; Install binaries from Release output
Source: "{#SourcePath}\bin\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Place config in ProgramData (only if not already there)
Source: "appsettings.json"; DestDir: "{commonappdata}\Virinco\WATS\LogFileCollector"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\LogFileCollector"; Filename: "{app}\LogFileCollector.exe"

[Run]
; Open appsettings.json in Notepad after install
Filename: "notepad.exe"; \
  Parameters: """{commonappdata}\Virinco\WATS\LogFileCollector\appsettings.json"""; \
  Description: "Edit configuration"; \
  Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Remove scheduled task on uninstall
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN \Virinco\LogFileCollector /F"; Flags: runhidden

[Tasks]
Name: createtask; Description: "Create scheduled task in Task Scheduler (\Virinco\LogFileCollector, run as NetworkService with --rescan)"; GroupDescription: "Additional tasks:"; Flags: unchecked

[Code]
procedure CreateTaskXml(const XmlPath, ExePath: string);
var
  SL: TStringList;
begin
  SL := TStringList.Create;
  try
    SL.Text :=
      '<?xml version="1.0" encoding="UTF-16"?>' + #13#10 +
      '<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' +
      '  <RegistrationInfo>' +
      '    <Author>Virinco AS</Author>' +
      '    <Description>LogFileCollector scheduled task</Description>' +
      '  </RegistrationInfo>' +
      '  <Triggers>' +
      '    <BootTrigger>' +
      '      <Enabled>true</Enabled>' +
      '    </BootTrigger>' +
      '  </Triggers>' +
      '  <Principals>' +
      '    <Principal id="Author">' +
      '      <UserId>NT AUTHORITY\NetworkService</UserId>' +
      '      <LogonType>ServiceAccount</LogonType>' +
      '      <RunLevel>LeastPrivilege</RunLevel>' +
      '    </Principal>' +
      '  </Principals>' +
      '  <Settings>' +
      '    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' +
      '    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' +
      '    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' +
      '    <AllowHardTerminate>true</AllowHardTerminate>' +
      '    <StartWhenAvailable>true</StartWhenAvailable>' +
      '    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>' +
      '    <IdleSettings>' +
      '      <StopOnIdleEnd>false</StopOnIdleEnd>' +
      '      <RestartOnIdle>false</RestartOnIdle>' +
      '    </IdleSettings>' +
      '    <AllowStartOnDemand>true</AllowStartOnDemand>' +
      '    <Enabled>true</Enabled>' +
      '    <Hidden>false</Hidden>' +
      '    <RunOnlyIfIdle>false</RunOnlyIfIdle>' +
      '    <WakeToRun>false</WakeToRun>' +
      '    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>' +
      '    <Priority>7</Priority>' +
      '  </Settings>' +
      '  <Actions Context="Author">' +
      '    <Exec>' +
      '      <Command>' + ExePath + '</Command>' +
      '      <Arguments>--rescan</Arguments>' +
      '    </Exec>' +
      '  </Actions>' +
      '</Task>';
    SL.SaveToFile(XmlPath);
  finally
    SL.Free;
  end;
end;

procedure CreateTaskViaXml();
var
  ResultCode: Integer;
  XmlPath, TaskExe, Args, ExePath: String;
begin
  TaskExe := ExpandConstant('{sys}\schtasks.exe');
  XmlPath := ExpandConstant('{tmp}\LogFileCollector.xml');
  ExePath := ExpandConstant('{app}\LogFileCollector.exe');

  CreateTaskXml(XmlPath, ExePath);

  { Remove old task if it exists }
  Exec(TaskExe, '/Delete /TN "\Virinco\LogFileCollector" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { Import new XML into folder \Virinco }
  Args := '/Create /TN "\Virinco\LogFileCollector" /RU "NT AUTHORITY\NetworkService" /XML "' + XmlPath + '" /F';

  if not Exec(TaskExe, Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    MsgBox('Failed to create scheduled task from XML.', mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('createtask') then
    CreateTaskViaXml();
end;
