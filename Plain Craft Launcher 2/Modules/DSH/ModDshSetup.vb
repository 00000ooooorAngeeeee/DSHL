' ============================================================================================
'  ModDshSetup —— 首次启动引导 与 整合包创建/导入向导（需求 4、5）
'
'  首次启动引导（DshEnsureFirstRun）在「启动」页加载时调用一次：
'    1. 检查 Node.js 运行环境，没有就引导下载
'    2. 检查版本仓库里有没有 dsh 版本，没有就引导去下载
'    3. 检查有没有整合包，没有就引导创建（可以选择"从现有 DSH_HOME 导入"）
'
'  全部用 PCL 自带的 MyMsgBox / MyMsgBoxInput / Dialogs.SelectFolder 实现，
'  不新增窗口，避免动 XAML。
' ============================================================================================
Imports Newtonsoft.Json.Linq

Public Module ModDshSetup

    Private IsFirstRunChecking As Boolean = False

    ''' <summary>
    ''' 首次启动引导。幂等：设置项 DshSetupFinished 为 True 且环境完整时直接返回。
    ''' 会在后台线程等待加载器，UI 弹窗通过 RunInUiWait 回到 UI 线程。
    ''' </summary>
    Public Sub DshEnsureFirstRun()
        '冷启动只检查一次
        Static HasChecked As Boolean = False
        If HasChecked Then Return
        HasChecked = True
        If Not DshModeEnabledSafe() Then Return

        RunInThread(
        Sub()
            Try
                If IsFirstRunChecking Then Return
                IsFirstRunChecking = True

                '等待整合包列表扫描完成
                DshInstanceListLoader.WaitForExit()
                Dim NeedNode As Boolean = Not DshNodeReady()
                Dim Installed As List(Of String) = DshInstalledVersions()
                Dim NeedVersion As Boolean = Installed.Count = 0
                Dim NeedInstance As Boolean = DshInstanceList.Count = 0
                If Not (NeedNode OrElse NeedVersion OrElse NeedInstance) Then
                    '环境完整，标记已完成
                    DshSetSetting("DshSetupFinished", True)
                    Return
                End If

                '1. 欢迎
                If Not DshSetting("DshSetupFinished", False) Then
                    RunInUiWait(Sub()
                                    MyMsgBox("欢迎使用 DeepSeekHarness 启动器！" & vbCrLf & vbCrLf &
                                             "这是一个由 PCL2 改造而来的启动器，用来管理和启动 DeepSeekHarness（dsh）。" & vbCrLf & vbCrLf &
                                             "接下来会用几步帮你把运行环境准备好。", "首次启动引导", "开始", "跳过")
                                End Sub)
                End If

                '2. Node.js
                If NeedNode Then
                    Dim Choice As Integer = 0
                    RunInUiWait(Sub()
                                    Choice = MyMsgBox("第 1 步：需要 Node.js 运行环境" & vbCrLf & vbCrLf &
                                                      "dsh 是 Node.js 程序，必须有 node.exe 才能运行。" & vbCrLf &
                                                      "本机尚未检测到 Node.js。" & vbCrLf & vbCrLf &
                                                      "是否现在从 npmmirror 镜像自动下载？" & vbCrLf &
                                                      "（会安装到 " & DshRuntimeRoot & "）", "首次启动引导（1/3）", "自动下载", "我自己指定", "跳过")
                                End Sub)
                    Select Case Choice
                        Case 1
                            DshNodeInstallLoader.Start(0, IsForceRestart:=True)
                            DshNodeInstallLoader.WaitForExit()
                            If DshNodeInstallLoader.State <> LoadState.Finished Then
                                Logger.Warn("自动安装 Node.js 未成功，改为手动指定")
                                NeedNode = True
                            Else
                                NeedNode = False
                            End If
                        Case 2
                            '手动指定 node.exe
                            Dim File As String = Nothing
                            RunInUiWait(Sub()
                                            Dim Files = Dialogs.SelectFile("选择 node.exe", False)
                                            If Files IsNot Nothing AndAlso Files.Any() Then File = Files(0)
                                        End Sub)
                            If File IsNot Nothing Then
                                DshSetSetting("DshRuntimeRoot", PathUtils.RemoveSlashSuffix(System.IO.Path.GetDirectoryName(File)))
                                NeedNode = Not DshNodeReady()
                            End If
                    End Select
                End If

                '3. dsh 版本
                If Not NeedNode AndAlso NeedVersion Then
                    Dim Choice As Integer = 0
                    RunInUiWait(Sub()
                                    Choice = MyMsgBox("第 2 步：安装 dsh 本体" & vbCrLf & vbCrLf &
                                                      "版本仓库里还没有任何 dsh 版本。" & vbCrLf &
                                                      "推荐先安装内置推荐版本 " & DshDefaultVersion & "（RC 候选版，相对稳定）。" & vbCrLf & vbCrLf &
                                                      "现在安装吗？也可以在「下载 → DSH 版本」里挑选其它版本。", "首次启动引导（2/3）", "安装推荐版本", "稍后自己选")
                                End Sub)
                    If Choice = 1 Then
                        DshVersionInstallLoader.Start(DshDefaultVersion, IsForceRestart:=True)
                        DshVersionInstallLoader.WaitForExit()
                        If DshVersionInstallLoader.State = LoadState.Finished Then NeedVersion = False
                    End If
                End If

                '4. 整合包
                If Not NeedNode AndAlso Not NeedVersion AndAlso NeedInstance Then
                    RunInUi(Sub() DshNewInstanceWizard())
                End If

                '收尾
                Dim AllReady As Boolean = DshNodeReady() AndAlso DshInstalledVersions().Count > 0 AndAlso DshInstanceList.Count > 0
                If AllReady Then
                    DshSetSetting("DshSetupFinished", True)
                    RunInUi(Sub()
                                Hint("运行环境已就绪，可以开始使用 DeepSeekHarness 了！", HintType.Green)
                                FrmLaunchLeft?.RefreshButtonsUI()
                            End Sub)
                End If
            Catch ex As Exception
                Logger.Error(ex, "首次启动引导出错")
            Finally
                IsFirstRunChecking = False
            End Try
        End Sub)
    End Sub

    Private Function DshModeEnabledSafe() As Boolean
        Try
            Return PageLaunchLeft.DshModeEnabled()
        Catch
            Return True
        End Try
    End Function

#Region "新建整合包向导"

    ''' <summary>
    ''' 新建整合包的交互向导。必须在 UI 线程调用。
    ''' </summary>
    Public Sub DshNewInstanceWizard()
        Try
            '1. 名称
            Dim DefaultName As String = DshNewInstanceName()
            Dim Name As String = MyMsgBoxInput("新建整合包", "给这个整合包起个名字（会作为文件夹名）。" & vbCrLf &
                                               "每个整合包都有独立的 dsh 版本、插件、技能与配置。", DefaultName,
                                               New ObjectModel.Collection(Of Validate) From {
                                                   New ValidateFunc(Function(v As String) If(String.IsNullOrWhiteSpace(v), "不能为空", Nothing)),
                                                   New ValidateLength(1, 60),
                                                   New ValidateExcept({"\", "/", ":", "*", "?", """", "<", ">", "|"})
                                               })
            If String.IsNullOrWhiteSpace(Name) Then Return
            Name = Name.Trim()

            '2. 绑定哪个 dsh 版本
            Dim Installed As List(Of String) = DshInstalledVersions()
            Dim DefaultVer As String = DshSetting("DshDefaultVersion", "")
            If String.IsNullOrWhiteSpace(DefaultVer) Then DefaultVer = If(Installed.Any(), Installed(0), DshDefaultVersion)
            Dim VersionTip As String =
                If(Installed.Any(),
                   "已安装的版本：" & vbCrLf & Installed.Take(10).Join(vbCrLf) & vbCrLf & vbCrLf & "输入要绑定的版本号：",
                   "版本仓库里还没有已安装的 dsh 版本。" & vbCrLf & "可以先填 " & DefaultVer & "，之后到「下载 → DSH 版本」里安装。")
            Dim Version As String = MyMsgBoxInput("选择 dsh 版本", VersionTip, DefaultVer,
                                                  New ObjectModel.Collection(Of Validate) From {New ValidateFunc(Function(v As String) If(String.IsNullOrWhiteSpace(v), "不能为空", Nothing))})
            If String.IsNullOrWhiteSpace(Version) Then Version = DefaultVer
            Version = Version.Trim()

            '3. 是否导入现有 DSH_HOME
            Dim ImportChoice As Integer = 0
            Dim ExistingHome As String = Environment.GetEnvironmentVariable("DSH_HOME", EnvironmentVariableTarget.User)
            If String.IsNullOrWhiteSpace(ExistingHome) Then ExistingHome = Environment.GetEnvironmentVariable("DSH_HOME")
            If String.IsNullOrWhiteSpace(ExistingHome) Then ExistingHome = Paths.AppData & ".dsh"
            If DirectoryUtils.Exists(ExistingHome) Then
                ImportChoice = MyMsgBox("检测到现有的 DSH_HOME：" & vbCrLf & ExistingHome & vbCrLf & vbCrLf &
                                        "是否把它里面的技能、插件、配置和凭证复制到新整合包？" & vbCrLf &
                                        "（只复制，不会修改或删除原目录）", "导入已有配置", "导入", "不导入（全新）")
            End If

            '4. 工作区
            Dim Workspace As String = ""
            If MyMsgBox("是否为这个整合包单独指定工作区（dsh 打开的文件夹）？" & vbCrLf &
                        "不指定则使用整合包目录下的 workspace\。", "工作区", "使用默认", "现在指定") = 2 Then
                Dim Files = Dialogs.SelectFolder("选择工作区文件夹", False)
                If Files IsNot Nothing AndAlso Files.Any() Then Workspace = Files(0)
            End If

            '5. 创建
            Dim Instance As DshInstance = DshCreateInstance(Name, Version, Workspace)
            If ImportChoice = 1 Then
                DshImportFromHome(Instance, ExistingHome, True, True, True)
            End If

            '6. 立刻为该整合包初始化 profile
            If DshVersionInstalled(Instance.DshVersion) Then
                DshEnsureProfile(Nothing, Instance)
            End If

            DshWriteManifest(Instance)
            DshRefreshInstanceList()
            DshInstanceListLoader.WaitForExit()

            '选中新整合包
            Dim Created As DshInstance = DshInstanceList.FirstOrDefault(Function(i) i.Name = Name)
            If Created IsNot Nothing Then
                DshInstanceSelected = Created
                If ImportChoice = 1 Then
                    Hint($"整合包「{Name}」创建完成，已导入现有配置", HintType.Green)
                Else
                    Hint($"整合包「{Name}」创建完成", HintType.Green)
                End If
                FrmLaunchLeft?.RefreshButtonsUI()
                If Not Created.IsVersionInstalled Then
                    If MyMsgBox($"整合包「{Name}」已创建，但它绑定的 dsh {Created.DshVersion} 还没有安装。" & vbCrLf &
                                "现在去下载页安装吗？", "还需要安装 dsh", "去下载", "稍后") = 1 Then
                        FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadDsh)
                    End If
                End If
            End If
        Catch ex As Exception
            Logger.Error(ex, "新建整合包失败")
            MyMsgBox(ex.Message, "新建整合包失败", IsWarn:=True)
        End Try
    End Sub

#End Region

End Module
