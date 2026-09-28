Imports System.Windows.Threading

Public Class PageLaunchLeft

    '加载当前版本
    Private IsLoad As Boolean = False
    Private IsLoadFinished As Boolean = False
    Public Sub PageLaunchLeft_Loaded() Handles Me.Loaded
        If IsLoad Then RefreshPage(True, False)

        AprilPosTrans.X = 0
        AprilPosTrans.Y = 0

        If IsLoad Then Return
        IsLoad = True
        AniControlEnabled += 1

        '开始按钮
        AddHandler McInstanceListLoader.LoadingStateChanged, AddressOf RefreshButtonsUI
        AddHandler McFolderListLoader.LoadingStateChanged, AddressOf RefreshButtonsUI
        AddHandler DshInstanceListLoader.LoadingStateChanged, AddressOf RefreshButtonsUI
        AddHandler DshVersionListLoader.LoadingStateChanged, AddressOf RefreshButtonsUI
        'BtnLaunch 的 Text 是依赖属性（没有 TextChanged 事件），
        '用 DependencyPropertyDescriptor 监听它，好让底部说明文字在按钮文案变化时重新居中
        '（文案变了按钮要重新测量，那行说明的可用宽度也跟着变）。
        Try
            Dim Dpd As ComponentModel.DependencyPropertyDescriptor =
                ComponentModel.DependencyPropertyDescriptor.FromProperty(MyButton.TextProperty, GetType(MyButton))
            If Dpd IsNot Nothing Then Dpd.AddValueChanged(BtnLaunch, Sub() DshRefreshLabVersionMargin())
        Catch ex As Exception
            Logger.Warn(ex, "挂接 BtnLaunch 文案变化监听失败")
        End Try
        RefreshButtonsUI()

        'DSH 模式：先扫整合包列表（不联网），并触发首次启动引导
        DshInstanceListLoader.Start(0)
        '挂上安装任务的模块级状态跟踪（任务管理器登记/清理、完成后刷新列表）
        DshInstallInit()
        DshEnsureFirstRun()

        '加载版本
        RunInNewThread(
        Sub()
            '自动整合包安装：准备
            Dim PackInstallPath As String = Nothing
            If FileUtils.Exists(Paths.Base & "modpack.zip") Then PackInstallPath = Paths.Base & "modpack.zip"
            If FileUtils.Exists(Paths.Base & "modpack.mrpack") Then PackInstallPath = Paths.Base & "modpack.mrpack"
            If PackInstallPath IsNot Nothing Then
                Logger.Warn($"需自动安装整合包：{PackInstallPath}")
                PageSelectLeft.CreateMcFolderInCurrentPath()
                McFolderListLoader.WaitForExit()
            End If
            '确认 Minecraft 文件夹存在
            If McFolderSelected = "" OrElse Not DirectoryUtils.Exists(McFolderSelected) Then
                '无效的文件夹
                If McFolderSelected = "" Then
                    Logger.Info("没有已储存的 Minecraft 文件夹")
                Else
                    Logger.Warn($"Minecraft 文件夹无效，该文件夹已不存在：{McFolderSelected}")
                End If
                McFolderListLoader.WaitForExit(IsForceRestart:=True)
                McFolderSelected = McFolderList.First.Location
            End If
            If Settings.Get(Of Boolean)("SystemDebugDelay") Then Thread.Sleep(RandomInteger(500, 3000))
            '自动整合包安装
            If PackInstallPath IsNot Nothing Then
                Try
                    Dim InstallLoader = ModpackInstall(PackInstallPath)
                    Logger.Info($"自动安装整合包已开始：{PackInstallPath}")
                    InstallLoader.WaitForExit()
                    If InstallLoader.State = LoadState.Finished Then
                        Logger.Info($"自动安装整合包成功，清理安装包：{PackInstallPath}")
                        FileUtils.Delete(PackInstallPath)
                    End If
                Catch ex As Exception
                    If ex.IsCanceled Then
                        Logger.Info($"自动安装整合包已取消：{PackInstallPath}")
                    Else
                        Logger.Error(ex, $"自动安装整合包失败：{PackInstallPath}", LogBehavior.Alert)
                    End If
                End Try
            End If
            '确认 Minecraft 版本存在
            Dim Selection As String = ReadIni(McFolderSelected & "PCL.ini", "Version")
            Dim Instance As McInstance = If(Selection = "", Nothing, New McInstance(Selection))
            If Instance Is Nothing OrElse Not Instance.PathVersion.StartsWithF(McFolderSelected) OrElse Not Instance.Check() Then
                '无效的版本
                Logger.Info($"当前选择的 Minecraft 版本无效：{If(Instance Is Nothing, "null", Instance.PathVersion)}", If(IsNothing(Instance), LogBehavior.None, LogBehavior.ToastIfDebug))
                If Not McInstanceListLoader.State = LoadState.Finished Then LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\", WaitForExit:=True)
                If Not McInstanceList.Any() OrElse McInstanceList.First.Value(0).Logo.Contains("RedstoneBlock") Then
                    Instance = Nothing
                    Logger.Info("无可用 Minecraft 版本")
                Else
                    Instance = McInstanceList.First.Value(0)
                    Logger.Info($"自动选择 Minecraft 版本：{Instance.PathVersion}")
                End If
            End If
            RunInUi(
            Sub()
                McInstanceSelected = Instance '绕这一圈是为了避免 McVersionCheck 触发第二次版本改变
                IsLoadFinished = True
                RefreshButtonsUI()
                RefreshPage(False, False) '有可能选择的版本变化了，需要重新刷新
                If McLoginAble() = "" Then McLoginLoader.Start() '自动登录
                '用于自动化测试生成的程序是否可以正常运行
                If Environment.CommandLine.Contains("--test") Then
                    RunInThread(
                    Sub()
                        Thread.Sleep(500)
                        RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.Download))
                        Thread.Sleep(500)
                        RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadMod))
                        Thread.Sleep(500)
                        RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.Setup))
                        Thread.Sleep(500)
                        RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.Other))
                        Thread.Sleep(500)
                        RunInUi(Sub() BtnVersion_Click())
                        Thread.Sleep(500)
                        RunInUi(Sub() BtnMore_Click())
                        Thread.Sleep(500)
                        FormMain.EndProgramForce(123)
                    End Sub)
                End If
            End Sub)
        End Sub, "Version Check", ThreadPriority.AboveNormal)

        '改变页面
        Dim LoginType = Settings.Get(Of McLoginType)("LoginType")
        If LoginType = McLoginType.Legacy OrElse LoginType = McLoginType.Ms Then CType(FindName("RadioLoginType" & LoginType), MyRadioButton).Checked = True
        RefreshPage(False, False)

        AniControlEnabled -= 1
    End Sub

#Region "切换大页面"

    ''' <summary>
    ''' 切换至启动中页面。
    ''' </summary>
    Public Sub PageChangeToLaunching()
        '修改登陆方式
        Select Case Settings.Get(Of McLoginType)("LoginType")
            Case McLoginType.Legacy
                LabLaunchingMethod.Text = "离线登录"
            Case McLoginType.Ms
                LabLaunchingMethod.Text = "正版登录"
            Case McLoginType.Nide
                LabLaunchingMethod.Text = "统一通行证"
            Case McLoginType.Auth
                LabLaunchingMethod.Text = "Authlib-Injector"
        End Select
        '初始化页面
        LabLaunchingName.Text = McInstanceSelected.Name
        LabLaunchingStage.Text = "初始化"
        LabLaunchingTitle.Text = If(CurrentLaunchOptions?.SaveBatch Is Nothing, "正在启动游戏", "正在导出启动脚本")
        LabLaunchingProgress.Text = "0.00 %"
        LabLaunchingProgress.Opacity = 1
        LabLaunchingDownload.Visibility = Visibility.Visible
        LabLaunchingProgressLeft.Opacity = 0.6
        LabLaunchingDownload.Visibility = Visibility.Visible
        LabLaunchingDownload.Text = "0 B/s"
        LabLaunchingDownload.Opacity = 0
        LabLaunchingDownload.Visibility = Visibility.Collapsed
        LabLaunchingDownloadLeft.Opacity = 0
        LabLaunchingDownloadLeft.Visibility = Visibility.Collapsed
        ProgressLaunchingFinished.Width = New GridLength(0, GridUnitType.Star)
        ProgressLaunchingUnfinished.Width = New GridLength(1, GridUnitType.Star)
        PanLaunchingHint.Opacity = 0
        PanLaunchingHint.Visibility = Visibility.Collapsed
        PanLaunchingInfo.Width = Double.NaN '重置宽度改变动画
        McLaunchProcess = Nothing
        McLaunchWatcher = Nothing
        '获取 “你知道吗” 提示
        LabLaunchingHint.Text = PageOtherTest.GetRandomHint()
        '初始化其他页面
        PanInput.IsHitTestVisible = False
        PanLaunching.IsHitTestVisible = False
        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Run
        PanLaunching.Visibility = Visibility.Visible
        AniStart({
                AaOpacity(PanInput, 0, 50), '略作延迟，这样如果预检测失败，不会出现奇怪的弹一下的动画
                AaOpacity(PanInput, -PanInput.Opacity, 110, , New AniEaseInFluent, True),
                AaScaleTransform(PanInput, 1.2 - CType(PanInput.RenderTransform, ScaleTransform).ScaleX, 160),
                AaOpacity(PanLaunching, 1 - PanLaunching.Opacity, 150, 100),
                AaScaleTransform(PanLaunching, 1 - CType(PanLaunching.RenderTransform, ScaleTransform).ScaleX, 500, 100, New AniEaseOutBack(AniEasePower.Weak)),
                AaCode(Sub() PanLaunching.IsHitTestVisible = True, 150)
            }, "Launch State Page")
    End Sub
    ''' <summary>
    ''' 切换至登录页面。
    ''' </summary>
    Public Sub PageChangeToLogin()
        PageGet(PageCurrent).Reload(KeepInput:=False)
        PanInput.IsHitTestVisible = False
        PanLaunching.IsHitTestVisible = False
        LoadLaunching.State.LoadingState = MyLoading.MyLoadingState.Stop
        PanInput.Visibility = Visibility.Visible
        AniStart({
            AaOpacity(PanLaunching, -PanLaunching.Opacity, 150),
            AaScaleTransform(PanLaunching, 0.8 - CType(PanLaunching.RenderTransform, ScaleTransform).ScaleX, 150,, New AniEaseOutFluent(AniEasePower.Weak)),
            AaOpacity(PanInput, 1 - PanInput.Opacity, 250, 50),
            AaScaleTransform(PanInput, 1 - CType(PanInput.RenderTransform, ScaleTransform).ScaleX, 300, 50, New AniEaseOutBack(AniEasePower.Weak)),
            AaCode(Sub() PanInput.IsHitTestVisible = True, 200)
        }, "Launch State Page", True)
    End Sub

#End Region

#Region "切换登录页面"

    Private Enum PageType
        None
        Legacy
        Nide
        NideSkin
        Auth
        AuthSkin
        Ms
        MsSkin
    End Enum
    ''' <summary>
    ''' 当前页面的种类。
    ''' </summary>
    Private PageCurrent As PageType = PageType.None

    Private Function PageGet(Type As PageType)
        Select Case Type
            Case PageType.Legacy
                If IsNothing(FrmLoginLegacy) Then FrmLoginLegacy = New PageLoginLegacy
                Return FrmLoginLegacy
            Case PageType.Nide
                If IsNothing(FrmLoginNide) Then FrmLoginNide = New PageLoginNide
                Return FrmLoginNide
            Case PageType.NideSkin
                If IsNothing(FrmLoginNideSkin) Then FrmLoginNideSkin = New PageLoginNideSkin
                Return FrmLoginNideSkin
            Case PageType.Auth
                If IsNothing(FrmLoginAuth) Then FrmLoginAuth = New PageLoginAuth
                Return FrmLoginAuth
            Case PageType.AuthSkin
                If IsNothing(FrmLoginAuthSkin) Then FrmLoginAuthSkin = New PageLoginAuthSkin
                Return FrmLoginAuthSkin
            Case PageType.Ms
                If IsNothing(FrmLoginMs) Then FrmLoginMs = New PageLoginMs
                Return FrmLoginMs
            Case PageType.MsSkin
                If IsNothing(FrmLoginMsSkin) Then FrmLoginMsSkin = New PageLoginMsSkin
                Return FrmLoginMsSkin
            Case Else
                Throw New ArgumentOutOfRangeException("Type", "即将切换的登录分页编号越界")
        End Select
    End Function
    ''' <summary>
    ''' 切换现有登录页面种类，返回新页面的实例。
    ''' </summary>
    ''' <param name="Type">新页面的种类。</param>
    ''' <param name="Anim">是否显示动画。</param>
    Private Function PageChange(Type As PageType, Anim As Boolean)
        Dim PageNew As Object = FrmLoginMs '初始化一个东西，避免在执行时出现异常导致雪崩
        Try

#Region "确定更改的页面实例并实例化"
            If PageCurrent = Type Then Return PageNew
            PageNew = PageGet(Type)
#End Region

#Region "切换页面"
            AniStop("FrmLogin PageChange")
            '清除页面关联性
            If Not IsNothing(PageNew) AndAlso Not IsNothing(PageNew.Parent) Then PageNew.SetValue(ContentPresenter.ContentProperty, Nothing)
            If Anim Then
                '动画
                Dispatcher.Invoke(
                Sub()
                    '执行动画
                    AniStart({
                        AaOpacity(PanLogin, -PanLogin.Opacity, 100,, New AniEaseOutFluent),
                        AaCode(
                        Sub()
                            AniControlEnabled += 1
                            PanLogin.Children.Clear()
                            PanLogin.Children.Add(PageNew)
                            AniControlEnabled -= 1
                        End Sub, 100),
                        AaOpacity(PanLogin, 1, 100, 120, New AniEaseInFluent)
                    }, "FrmLogin PageChange")
                End Sub, DispatcherPriority.Render)
            Else
                '无动画
                AniControlEnabled += 1
                PanLogin.Children.Clear()
                PanLogin.Children.Add(PageNew)
                AniControlEnabled -= 1
            End If
#End Region

            PageCurrent = Type
            Return PageNew
        Catch ex As Exception
            Logger.Error(ex, $"切换登录分页失败（{Type}）")
            Return PageNew
        End Try
    End Function

    ''' <summary>
    ''' 确认当前显示的子页面正确，并刷新该页面。
    ''' </summary>
    Public Sub RefreshPage(KeepInput As Boolean, Anim As Boolean)
        '获取页面的可用种类并回写缓存
        Dim Type As PageType
        Dim LoginPageType As Integer
        If McInstanceSelected IsNot Nothing Then
            LoginPageType = Settings.Get(Of Integer)("VersionServerLogin", Instance:=McInstanceSelected)
            '缓存当前版本的页面种类，下一次打开 McInstanceSelected 为空时才能加载出正确的页面
            Settings.Set("LoginPageType", LoginPageType)
        Else
            LoginPageType = Settings.Get(Of Integer)("LoginPageType")
        End If
        Select Case LoginPageType
            Case 0 '正版或离线
UnknownType:
                If RadioLoginType5.Checked Then
                    If Settings.Get(Of String)("CacheMsV2Access") = "" Then
                        Type = PageType.Ms
                    Else
                        Type = PageType.MsSkin
                    End If
                    Settings.Set("LoginType", McLoginType.Ms)
                Else
                    Type = PageType.Legacy
                    Settings.Set("LoginType", McLoginType.Legacy)
                End If
                PanType.Visibility = If(DshModeEnabled(), Visibility.Collapsed, Visibility.Visible)
                PanTypeOne.Visibility = Visibility.Collapsed
                RadioLoginType5.Visibility = Visibility.Visible
                RadioLoginType0.Visibility = Visibility.Visible
            Case 1 '仅正版
                If Settings.Get(Of String)("CacheMsV2Access") = "" Then
                    Type = PageType.Ms
                Else
                    Type = PageType.MsSkin
                End If
                Settings.Set("LoginType", McLoginType.Ms)
                PanType.Visibility = Visibility.Collapsed
                PanTypeOne.Visibility = Visibility.Visible
                PathTypeOne.Data = (New GeometryConverter).ConvertFromString(Logo.IconButtonShield)
                LabTypeOne.Text = "正版登录"
                RadioLoginType5.Visibility = Visibility.Visible
                RadioLoginType0.Visibility = Visibility.Collapsed
            Case 2 '仅离线
                Type = PageType.Legacy
                Settings.Set("LoginType", McLoginType.Legacy)
                PanType.Visibility = Visibility.Collapsed
                PanTypeOne.Visibility = Visibility.Visible
                PathTypeOne.Data = (New GeometryConverter).ConvertFromString(Logo.IconButtonOffline)
                LabTypeOne.Text = "离线登录"
            Case 3 '统一通行证
                If Settings.Get(Of String)("CacheNideAccess") = "" Then
                    Type = PageType.Nide
                Else
                    Type = PageType.NideSkin
                End If
                Settings.Set("LoginType", McLoginType.Nide)
                PanType.Visibility = Visibility.Collapsed
                PanTypeOne.Visibility = Visibility.Visible
                PathTypeOne.Data = (New GeometryConverter).ConvertFromString(Logo.IconButtonCard)
                LabTypeOne.Text = "统一通行证登录"
            Case 4 'Authlib-Injector
                If Settings.Get(Of String)("CacheAuthAccess") = "" Then
                    Type = PageType.Auth
                Else
                    Type = PageType.AuthSkin
                End If
                Settings.Set("LoginType", McLoginType.Auth)
                PanType.Visibility = Visibility.Collapsed
                PanTypeOne.Visibility = Visibility.Visible
                PathTypeOne.Data = (New GeometryConverter).ConvertFromString(Logo.IconButtonCard)
                LabTypeOne.Text = If(McInstanceSelected Is Nothing, Settings.Get(Of String)("CacheAuthServerName"), Settings.Get(Of String)("VersionServerAuthName", Instance:=McInstanceSelected))
                If LabTypeOne.Text = "" Then LabTypeOne.Text = "第三方登录"
            Case Else
                Logger.Error($"未知的登录页面：{LoginPageType}", LogBehavior.Toast)
                GoTo UnknownType
        End Select
        '刷新页面
        If PageCurrent = Type Then Return
        PageChange(Type, Anim).Reload(KeepInput)
        Dim Control As MyRadioButton = FindName("RadioLoginType" & Settings.Get(Of McLoginType)("LoginType"))
        If Control IsNot Nothing Then Control.Checked = True
    End Sub
    Private Sub RadioLoginType_Change(sender As Object, raiseByMouse As Boolean) Handles RadioLoginType0.Check, RadioLoginType5.Check
        If raiseByMouse Then RefreshPage(True, True)
    End Sub

#End Region

#Region "皮肤"

    '微软正版皮肤
    Public Shared SkinMs As New LoaderTask(Of (String, String), String)("Loader Skin Ms", AddressOf SkinMsLoad, AddressOf SkinMsInput, ThreadPriority.AboveNormal)
    Private Shared Function SkinMsInput() As (String, String)
        '获取名称
        Return (Settings.Get(Of String)("CacheMsV2Name"), Settings.Get(Of String)("CacheMsV2Uuid"))
    End Function
    Private Shared Sub SkinMsLoad(Data As LoaderTask(Of (String, String), String))
        '清空已有皮肤
        '如果在输入时清空皮肤，若输入内容一样则不会执行 Load 方法，导致皮肤不被加载
        RunInUi(Sub() If FrmLoginMsSkin IsNot Nothing AndAlso FrmLoginMsSkin.Skin IsNot Nothing Then FrmLoginMsSkin.Skin.Clear())
        '获取 Url
        Dim UserName As String = Data.Input.Item1
        Dim Uuid As String = Data.Input.Item2
        If UserName = "" Then
            Data.Output = PathImage & "Skins/" & McSkinSex(McLoginLegacyUuid(UserName)) & ".png"
            Logger.Info("获取微软正版皮肤失败，ID 为空")
            GoTo Finish
        End If
        Try
            Dim Result As String = McSkinGetAddress(Uuid, "Ms")
            If Data.IsCanceled Then Throw New OperationCanceledException("当前任务已取消：" & UserName)
            Result = McSkinDownload(Result)
            If Data.IsCanceled Then Throw New OperationCanceledException("当前任务已取消：" & UserName)
            Data.Output = Result
        Catch ex As Exception
            If ex.IsCanceled Then
                Data.Output = ""
                Return
            ElseIf ex.GetDisplay(False).Contains("(429)") Then
                Data.Output = PathImage & "Skins/" & McSkinSex(McLoginLegacyUuid(UserName)) & ".png"
                Logger.Error($"获取正版皮肤失败（{UserName}）：获取皮肤太过频繁，请 5 分钟后再试！", LogBehavior.Toast)
            ElseIf ex.GetDisplay(False).Contains("未设置自定义皮肤") Then
                Data.Output = PathImage & "Skins/" & McSkinSex(McLoginLegacyUuid(UserName)) & ".png"
                Logger.Info("用户未设置自定义皮肤，跳过皮肤加载")
            Else
                Data.Output = PathImage & "Skins/" & McSkinSex(McLoginLegacyUuid(UserName)) & ".png"
                Logger.Error(ex, $"获取微软正版皮肤失败（{UserName}）", LogBehavior.Toast)
            End If
        End Try
Finish:
        '刷新显示
        If FrmLoginMsSkin IsNot Nothing Then
            RunInUi(AddressOf FrmLoginMsSkin.Skin.Load)
        ElseIf Not Data.IsCanceled Then '如果已经中断，Input 也被清空，就不会再次刷新
            Data.Input = Nothing '清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
        End If
    End Sub

    '离线皮肤
    Public Shared SkinLegacy As New LoaderTask(Of (String, String), String)("Loader Skin Legacy", AddressOf SkinLegacyLoad, AddressOf SkinLegacyInput, ThreadPriority.AboveNormal)
    Private Shared Function SkinLegacyInput() As (String, String)
        '根据类型判断输入
        Dim Type As Integer = Settings.Get(Of Integer)("LaunchSkinType")
        Select Case Type
            Case 0
                If FrmLoginLegacy?.IsReloaded Then
                    Return ("0", If(FrmLoginLegacy.ComboName.Text.Trim, ""))
                ElseIf Settings.Get(Of String)("LoginLegacyName") = "" Then
                    Return ("0", "")
                Else
                    Return ("0", If(Settings.Get(Of String)("LoginLegacyName").ToString.BeforeFirst("¨"), ""))
                End If
            Case 3
                Return ("3", Settings.Get(Of String)("LaunchSkinID"))
            Case Else
                Return (Type.ToString, "")
        End Select
    End Function
    Private Shared Sub SkinLegacyLoad(Data As LoaderTask(Of (String, String), String))
        '清空已有皮肤
        RunInUi(Sub() If FrmLoginLegacy IsNot Nothing AndAlso FrmLoginLegacy.Skin IsNot Nothing Then FrmLoginLegacy.Skin.Clear())
        '获取 Url
        Select Case CInt(Data.Input.Item1)
            Case 0 '默认
                Data.Output = PathImage & "Skins/" & McSkinSex(McLoginLegacyUuid(Data.Input.Item2)) & ".png"
            Case 1 'Steve
UseDefault:
                Data.Output = PathImage & "Skins/Steve.png"
            Case 2 'Alex
                Data.Output = PathImage & "Skins/Alex.png"
            Case 3 '正版
                Dim ID As String = Data.Input.Item2
                Try
                    If ID.Count < 2 Then
                        Data.Output = PathImage & "Skins/Steve.png"
                    Else
                        Dim Result As String = McLoginMojangUuid(ID, True)
                        If Data.IsCanceled Then Throw New OperationCanceledException("当前任务已取消：" & ID)
                        Result = McSkinGetAddress(Result, "Mojang")
                        If Data.IsCanceled Then Throw New OperationCanceledException("当前任务已取消：" & ID)
                        Result = McSkinDownload(Result)
                        If Data.IsCanceled Then Throw New OperationCanceledException("当前任务已取消：" & ID)
                        Data.Output = Result
                    End If
                Catch ex As Exception
                    If ex.IsCanceled Then
                        Data.Output = ""
                        Return
                    ElseIf ex.GetDisplay(False).Contains("(429)") Then
                        Data.Output = PathImage & "Skins/" & McSkinSex(McLoginLegacyUuid(ID)) & ".png"
                        Logger.Info($"获取离线登录使用的正版皮肤失败（{ID}）：获取皮肤太过频繁，请 5 分钟后再试！")
                    Else
                        Data.Output = PathImage & "Skins/" & McSkinSex(McLoginLegacyUuid(ID)) & ".png"
                        Logger.Warn(ex, $"获取离线登录使用的正版皮肤失败（{ID}）")
                    End If
                End Try
            Case 4 '自定义
                If Not FileUtils.Exists(Paths.AppDataThenName & "CustomSkin.png") Then
                    Hint("未找到离线皮肤自定义文件，可能它已被删除。PCL 将使用默认的 Steve 皮肤！")
                    Settings.Set("LaunchSkinType", 1)
                    GoTo UseDefault
                End If
                Data.Output = Paths.AppDataThenName & "CustomSkin.png"
        End Select
        '刷新显示
        If FrmLoginLegacy IsNot Nothing Then
            RunInUi(AddressOf FrmLoginLegacy.Skin.Load)
        ElseIf Not Data.IsCanceled Then '如果已经中断，Input 也被清空，就不会再次刷新
            Data.Input = Nothing '清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
        End If
    End Sub

    '统一通行证皮肤
    Public Shared SkinNide As New LoaderTask(Of (String, String), String)("Loader Skin Nide", AddressOf SkinNideLoad, AddressOf SkinNideInput, ThreadPriority.AboveNormal)
    Private Shared Function SkinNideInput() As (String, String)
        '获取名称
        Return (Settings.Get(Of String)("CacheNideName"), Settings.Get(Of String)("CacheNideUuid"))
    End Function
    Private Shared Sub SkinNideLoad(Data As LoaderTask(Of (String, String), String))
        '清空已有皮肤
        '如果在输入时清空皮肤，若输入内容一样则不会执行 Load 方法，导致皮肤不被加载
        RunInUi(Sub() If FrmLoginNideSkin IsNot Nothing AndAlso FrmLoginNideSkin.Skin IsNot Nothing Then FrmLoginNideSkin.Skin.Clear())
        '获取 Url
        Dim UserName As String = Data.Input.Item1
        Dim Uuid As String = Data.Input.Item2
        If UserName = "" Then
            Data.Output = PathImage & "Skins/" & McSkinSex(McLoginLegacyUuid(UserName)) & ".png"
            Logger.Info("获取统一通行证皮肤失败，ID 为空")
            GoTo Finish
        End If
        Try
            Dim Result As String = McSkinGetAddress(Uuid, "Nide")
            If Data.IsCanceled Then Throw New OperationCanceledException("当前任务已取消：" & UserName)
            Result = McSkinDownload(Result)
            If Data.IsCanceled Then Throw New OperationCanceledException("当前任务已取消：" & UserName)
            Data.Output = Result
        Catch ex As Exception
            If ex.IsCanceled Then
                Data.Output = ""
                Return
            ElseIf ex.GetDisplay(False).Contains("(429)") Then
                Data.Output = PathImage & "Skins/Steve.png"
                Logger.Error($"获取统一通行证皮肤失败（{UserName}）：获取皮肤太过频繁，请 5 分钟后再试！", LogBehavior.Toast)
            ElseIf ex.GetDisplay(False).Contains("未设置自定义皮肤") Then
                Data.Output = PathImage & "Skins/Steve.png"
                Logger.Info("用户未设置自定义皮肤，跳过皮肤加载")
            Else
                Data.Output = PathImage & "Skins/Steve.png"
                Logger.Error(ex, $"获取统一通行证皮肤失败（{UserName}）", LogBehavior.Toast)
            End If
        End Try
Finish:
        '刷新显示
        If FrmLoginNideSkin IsNot Nothing Then
            RunInUi(AddressOf FrmLoginNideSkin.Skin.Load)
        ElseIf Not Data.IsCanceled Then '如果已经中断，Input 也被清空，就不会再次刷新
            Data.Input = Nothing '清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
        End If
    End Sub

    'Authlib-Injector 皮肤
    Public Shared SkinAuth As New LoaderTask(Of (String, String), String)("Loader Skin Auth", AddressOf SkinAuthLoad, AddressOf SkinAuthInput, ThreadPriority.AboveNormal)
    Private Shared Function SkinAuthInput() As (String, String)
        '获取名称
        Return (Settings.Get(Of String)("CacheAuthName"), Settings.Get(Of String)("CacheAuthUuid"))
    End Function
    Private Shared Sub SkinAuthLoad(Data As LoaderTask(Of (String, String), String))
        '清空已有皮肤
        '如果在输入时清空皮肤，若输入内容一样则不会执行 Load 方法，导致皮肤不被加载
        RunInUi(Sub() If FrmLoginAuthSkin IsNot Nothing AndAlso FrmLoginAuthSkin.Skin IsNot Nothing Then FrmLoginAuthSkin.Skin.Clear())
        '获取 Url
        Dim UserName As String = Data.Input.Item1
        Dim UUID As String = Data.Input.Item2
        If UserName = "" Then
            Data.Output = PathImage & "Skins/Steve.png"
            Logger.Info("获取 Authlib-Injector 皮肤失败，ID 为空")
            GoTo Finish
        End If
        Try
            Dim Result As String = McSkinGetAddress(UUID, "Auth")
            If Data.IsCanceled Then Throw New OperationCanceledException("当前任务已取消：" & UserName)
            Result = McSkinDownload(Result)
            If Data.IsCanceled Then Throw New OperationCanceledException("当前任务已取消：" & UserName)
            Data.Output = Result
        Catch ex As Exception
            If ex.IsCanceled Then
                Data.Output = ""
                Return
            ElseIf ex.GetDisplay(False).Contains("(429)") Then
                Data.Output = PathImage & "Skins/Steve.png"
                Logger.Error($"获取 Authlib-Injector 皮肤失败（{UserName}）：获取皮肤太过频繁，请 5 分钟后再试！", LogBehavior.Toast)
            ElseIf ex.GetDisplay(False).Contains("未设置自定义皮肤") Then
                Data.Output = PathImage & "Skins/Steve.png"
                Logger.Info("用户未设置自定义皮肤，跳过皮肤加载")
            Else
                Data.Output = PathImage & "Skins/Steve.png"
                Logger.Error(ex, $"获取 Authlib-Injector 皮肤失败（{UserName}）", LogBehavior.Toast)
            End If
        End Try
Finish:
        '刷新显示
        If FrmLoginAuthSkin IsNot Nothing Then
            RunInUi(AddressOf FrmLoginAuthSkin.Skin.Load)
        ElseIf Not Data.IsCanceled Then '如果已经中断，Input 也被清空，就不会再次刷新
            Data.Input = Nothing '清空输入，因为皮肤实际上没有被渲染，如果不清空切换到页面的 Start 会由于输入相同而不渲染
        End If
    End Sub

    '全部皮肤加载器
    '需要放在其中元素的后面，否则会因为它提前被加载而莫名其妙变成 Nothing
    Public Shared SkinLoaders As New List(Of LoaderTask(Of (String, String), String)) From {SkinMs, SkinLegacy, SkinNide, SkinAuth}

#End Region

    '版本选择按钮（DSH 模式下复用为「整合包管理」入口）
    Private Sub BtnVersion_Click() Handles BtnVersion.Click
        If DshModeEnabled() Then
            'DSH 模式：直接进整合包管理页。
            '必须走 Setup 的子页面路由：DshManager 这个顶级页枚举值是 10，
            '而 FormMain.PageChange 会拿它当 PanTitleSelect.Children 的下标（顶部导航只有 5 个按钮），
            '直接 PageChange(PageType.DshManager) 会抛 ArgumentOutOfRangeException（实机踩过）。
            If FrmDshManager Is Nothing Then FrmDshManager = New PageDshManager
            FrmDshManager.LoadInstance(DshInstanceSelected)
            FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupManager)
            Return
        End If
        If McLaunchLoader.State = LoadState.Loading Then Return
        FrmMain.PageChange(FormMain.PageType.InstanceSelect)
    End Sub
    '启动按钮
    Public Sub LaunchButtonClick() Handles BtnLaunch.Click, BtnLaunch2.Click
        '守卫：页面正在切换时不响应；按钮被禁用时不响应。
        '注意要**按当前可见的那套布局**取按钮（原版只检查 BtnLaunch，
        '但状态 2 用的是 BtnLaunch2，只查 BtnLaunch 会漏掉禁用判断）。
        Dim ActiveBtn As MyButton = If(PanState2.Visibility = Visibility.Visible, BtnLaunch2, BtnLaunch)
        If McLaunchLoader.State = LoadState.Loading OrElse Not ActiveBtn.IsEnabled OrElse
            (FrmMain.PageRight IsNot Nothing AndAlso FrmMain.PageRight.PageState <> MyPageRight.PageStates.ContentStay AndAlso FrmMain.PageRight.PageState <> MyPageRight.PageStates.ContentEnter) Then Return
        '愚人节处理
        If IsAprilEnabled AndAlso Not IsAprilGiveup Then
            ThemeUnlock(12, False, "隐藏主题 滑稽彩 已解锁！")
            IsAprilGiveup = True
            Settings.Set("AprilYear", Date.Now.Year)
            FrmLaunchLeft.AprilScaleTrans.ScaleX = 1
            FrmLaunchLeft.AprilScaleTrans.ScaleY = 1
            FrmLaunchLeft.AprilPosTrans.X = 0
            FrmLaunchLeft.AprilPosTrans.Y = 0
            FrmMain.BtnExtraApril.ShowRefresh()
        End If
        '实际的启动
        If DshModeEnabled() Then
            '=== DSH 模式：启动 / 打开 / 新建 / 下载 ===
            '
            '★ 分派依据必须是**真实状态**，不能靠按钮文案（实机踩坑）：
            '  原来这里是 `Select Case BtnLaunch.Text`，我加第二套布局时改成了
            '  `Select Case If(BtnLaunch2.Visibility=Visible, BtnLaunch2.Text, BtnLaunch.Text)`，
            '  那个条件写错了 —— 状态 1（dsh 未运行、按钮写着「启动 DeepSeekHarness」）下
            '  会走到 "打开 DSH 页面" 分支，于是点「启动」变成"用缓存的地址开浏览器"，
            '  dsh 根本没启动（用户实测：浏览器打开 127.0.0.1:3081 无法访问）。
            '  → 改成按 DshInstanceIsAlive / 是否已装版本 判断，文案只用于界面显示。
            If DshInstanceSelected Is Nothing Then
                '还没有整合包：按钮表现为「新建整合包」
                DshNewInstanceWizard()
                RefreshButtonsUI()
                Return
            End If
            If Not DshInstanceSelected.IsVersionInstalled Then
                '绑定的 dsh 版本还没装：按钮表现为「下载 dsh」
                FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadDsh)
                Return
            End If
            If DshInstanceIsAlive(DshInstanceSelected) Then
                '已在运行（**用端口探测判断**，这样外部启动的 dsh 也能识别）：只开浏览器
                DshOpenBrowser(If(DshWebUrl <> "", DshWebUrl, DshLocalUrl(DshInstanceSelected.Port)))
                Return
            End If
            '其余情况：真正启动 dsh
            DshLaunchStart(DshInstanceSelected)
            Return
        End If

        '=== 原版模式：启动 Minecraft ===
        If BtnLaunch.Text = "启动游戏" Then
            McLaunchStart()
        ElseIf BtnLaunch.Text = "下载游戏" Then
            FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall)
        End If
    End Sub
    Private BtnLaunchState As Integer = 0
    Private BtnLaunchInstance As McInstance = Nothing

    ''' <summary>
    ''' 是否处于 DSH 模式（本魔改版默认启用）。
    ''' 启用后：启动按钮变为"启动 DeepSeekHarness"、版本选择变为整合包选择、
    ''' Minecraft 的账号/皮肤/联机界面不再显示。
    ''' </summary>
    Public Shared Function DshModeEnabled() As Boolean
        Return DshSetting("DshMode", True)
    End Function

    ''' <summary>
    ''' DSH 模式下的启动按钮状态刷新。
    ''' </summary>
    Private Sub RefreshDshButtonsUI()
        If Not BtnLaunch.IsLoaded Then Return

        Dim Instance As DshInstance = DshInstanceSelected
        Dim Loading_ As Boolean = (DshInstanceListLoader.State = LoadState.Loading)

        '当前状态：0 加载中 / 1 无整合包 / 2 有整合包但不能启动 / 3 可启动
        Dim CurrentState As Integer
        If Loading_ Then
            CurrentState = 0
        ElseIf Instance Is Nothing Then
            CurrentState = 1
        ElseIf Not Instance.CanLaunch Then
            CurrentState = 2
        Else
            CurrentState = 3
        End If

        '★ 用 DshInstanceIsAlive（内存引用 + **端口探测**）而不是只看 DshIsRunning。
        '  用户最初就要求"检测该整合包的端口号下是否有 dsh 正在运行" ——
        '  DshIsRunning 只看启动器内存里的进程引用，对"外部启动的 dsh / 引用已丢失"无效，
        '  会出现"端口明明有 dsh 在应答，界面却显示未运行"。
        Dim DshAlive As Boolean = DshInstanceIsAlive(Instance)
        Dim StateKey As String = CurrentState & "|" & If(Instance Is Nothing, "", Instance.PathInstance) & "|" & DshAlive.ToString()
        If StateKey = DshBtnLastKey Then
            '即使状态没变，运行中的按钮文案仍要刷新
            If DshAlive Then BtnLaunch.Text = "打开 DSH 页面"
            GoTo ExitRefresh
        End If
        DshBtnLastKey = StateKey

        Select Case CurrentState
            Case 0
                Logger.Info("启动按钮：正在加载整合包列表")
                BtnLaunch.Text = "正在加载"
                BtnLaunch.IsEnabled = False
                LabVersion.Text = "正在加载整合包列表，请稍候"
                BtnMore.Visibility = Visibility.Collapsed
                DshSetButtonState(False)
            Case 1
                Logger.Info("启动按钮：还没有任何整合包")
                BtnLaunch.Text = "新建整合包"
                BtnLaunch.IsEnabled = True
                LabVersion.Text = "还没有整合包，点一下新建一个"
                BtnMore.Visibility = Visibility.Collapsed
                DshSetButtonState(False)
            Case 2
                Logger.Info($"启动按钮：整合包 {Instance.Name} 尚不可启动（{If(Instance.ErrorMessage, "无错误信息")}）")
                If Not Instance.IsVersionInstalled Then
                    BtnLaunch.Text = "下载 dsh"
                    LabVersion.Text = $"整合包「{Instance.Name}」绑定的 dsh {Instance.DshVersion} 尚未安装，点此去安装"
                Else
                    BtnLaunch.Text = "启动 DeepSeekHarness"
                    BtnLaunch.IsEnabled = False
                    LabVersion.Text = $"整合包「{Instance.Name}」不可用：{If(String.IsNullOrWhiteSpace(Instance.ErrorMessage), "未知原因", Instance.ErrorMessage)}"
                End If
                BtnLaunch.IsEnabled = True
                BtnMore.Visibility = Visibility.Collapsed
                DshSetButtonState(False)
            Case 3
                Logger.Info($"启动按钮：整合包 {Instance.Name}（dsh {Instance.DshVersion}）")
                BtnLaunch.IsEnabled = True
                '用户要求：运行中这里就是"打开 dsh 页面"，关闭由旁边那个红色按钮负责
                BtnLaunch.Text = If(DshAlive, "打开 DSH 页面", "启动 DeepSeekHarness")
                LabVersion.Text = $"{Instance.Name}　·　dsh {Instance.DshVersion}　·　端口 {Instance.Port}"
                BtnMore.Visibility = Visibility.Visible
                '运行中切到「状态 2」布局（打开 / 关闭 + 整合包管理），否则用「状态 1」整行单按钮
                DshSetButtonState(DshAlive)
                '状态 2 用的那个「打开 DSH 页面」按钮要确保可点击（它默认是启用的，
                '但经历过状态 2→1→2 之后要复位，避免上次被禁用后一直点不动）
                BtnLaunch2.IsEnabled = True
        End Select

        '新建整合包在状态 1 时也允许点击
        If CurrentState = 1 Then BtnLaunch.IsEnabled = True

ExitRefresh:
        '功能隐藏
        '「整合包管理」有两个入口（两个状态各一个），同一时刻只显示当前状态的那个：
        '  · 状态 1：底部那个整行按钮 + 下面独立的 BtnVersion（即原来的「版本选择」，DSH 模式下复用为整合包管理）
        '  · 状态 2：PanState2 里的 BtnVersion2（在「打开/关闭」下方，与它们同宽）
        '所以状态 2 下要把 BtnVersion 收起来，否则会同时出现两个「整合包管理」。
        Dim WantFuncBtn As Visibility =
            If(Not PageSetupUI.HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenFunctionSelect"), Visibility.Collapsed, Visibility.Visible)
        If PanState2.Visibility = Visibility.Visible Then WantFuncBtn = Visibility.Collapsed
        BtnVersion.Visibility = WantFuncBtn
        'DSH 魔改：把「版本选择」复用为「整合包管理」的入口。
        '原因（用户要求）：顶部导航的「更多」页在 DSH 模式下要隐藏，而整合包管理原本挂在
        '「更多」页的左栏里，隐藏后就进不去了。这里改文案 + 直连管理页，入口反而更显眼。
        '注意「任务管理」不受影响：右上角那个按钮（BtnExtraDownload，ToolTip 就是"任务管理"）是它的入口。
        BtnVersion.Text = "整合包管理"
        BtnMore.Text = "整合包管理"
        BtnVersion.IsEnabled = True
        '只要已经有整合包（状态 2 或 3），就允许进整合包管理页：
        '状态 2 时用户往往正需要进去改绑定的 dsh 版本、管理插件/技能，所以这里也要显示。
        '(CurrentState 为 0/1 时下方也不会把它设成可见，所以这里不必额外判空。)
        If CurrentState >= 2 AndAlso CurrentState <= 3 Then BtnMore.Visibility = BtnVersion.Visibility
        '状态 2 里的「整合包管理」同样受"功能隐藏 → 版本选择"开关控制
        BtnVersion2.Visibility = If(PageSetupUI.HiddenForceShow OrElse Not Settings.Get(Of Boolean)("UiHiddenFunctionSelect"),
                                    Visibility.Visible, Visibility.Collapsed)
        'DSH 模式下不需要账号界面：把整个登录区（PanLoginArea）收起来。
        '为什么包一层统一开关、而不是分别设 PanLogin / PanType / PanTypeOne：
        'PCL 会在 RefreshPage 的多条分支里给 PanType 赋 Visibility（例如 UnknownType 分支设成 Visible），
        '逐个打补丁既容易漏，又会出现"第一次进启动页藏住了、从别的页面返回又冒出来"这种不一致。
        PanLogin.IsHitTestVisible = False
        PanLoginArea.Visibility = Visibility.Collapsed
        '底部只留一个入口：BtnMore 与 BtnVersion 在 DSH 模式下功能重复，隐藏 BtnMore
        BtnMore.Visibility = Visibility.Collapsed
    End Sub
    Private DshBtnLastKey As String = ""

    ''' <summary>
    ''' 在「状态 1」与「状态 2」两套底部布局之间切换。
    '''
    ''' 状态 1（dsh 未运行）：PanState1 → 整行一个按钮（启动 / 新建整合包 / 下载 dsh）
    ''' 状态 2（dsh 运行中）：PanState2 → 第 0 行「打开 DSH 页面」+「关闭 DSH」，第 2 行「整合包管理」
    '''
    ''' 为什么改成两套布局（用户建议，见 DEVNOTES #107）：
    '''   状态 2 需要「整合包管理」左边缘对齐蓝按钮、右边缘对齐红按钮。
    '''   用"一套布局 + 动态调宽度/边距"试了三版都没稳定做对（绑定方向、Auto 列挤压、
    '''   星号列测量循环……），而 **两套独立布局里状态 2 是 2×2 网格**：
    '''   「整合包管理」跨全部 3 列，宽度天然等于上面两个按钮的合计宽度，两边自动对齐，
    '''   一个绑定都不需要。
    '''
    ''' 切换只动画透明度，不动宽高 —— 避免动画中途的布局重叠（DEVNOTES #98）。
    ''' </summary>
    Private Sub DshSetButtonState(Running As Boolean)
        If Running = (PanState2.Visibility = Visibility.Visible) Then Return
        Try
            AniStop("FrmLaunchLeft DshBtnState")
            If Running Then
                PanState2.Visibility = Visibility.Visible
                PanState1.Visibility = Visibility.Collapsed
                LabVersion2.Text = DshShortVersionText()
                AniStart({
                    AaOpacity(PanState2, 1 - PanState2.Opacity, 120),
                    AaOpacity(PanState1, -PanState1.Opacity, 120)
                }, "FrmLaunchLeft DshBtnState")
            Else
                PanState1.Visibility = Visibility.Visible
                AniStart({
                    AaOpacity(PanState1, 1 - PanState1.Opacity, 120),
                    AaOpacity(PanState2, -PanState2.Opacity, 120),
                    AaCode(Sub()
                               PanState2.Visibility = Visibility.Collapsed
                               PanState2.Opacity = 0
                           End Sub, 130)
                }, "FrmLaunchLeft DshBtnState")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "切换启动页按钮布局失败")
            '动画失败也要保证状态正确
            PanState2.Visibility = If(Running, Visibility.Visible, Visibility.Collapsed)
            PanState1.Visibility = If(Running, Visibility.Collapsed, Visibility.Visible)
            PanState2.Opacity = If(Running, 1, 0)
            PanState1.Opacity = If(Running, 0, 1)
        End Try
    End Sub

    ''' <summary>
    ''' 状态 2 里那行灰色说明文字。
    ''' **必须短**：它是显示在「打开 DSH 页面」按钮内部的，而那个按钮在双按钮布局下
    ''' 只有大约 160 逻辑像素宽（左栏整行 260 减去「关闭 DSH」和 10 间距），
    ''' 再减 Padding 30×2，可用文字宽度只有约 100 像素。
    ''' 原来那串「My　·　dsh 0.1.7-rc.1　·　端口 3081」约 200 像素，必然被截断
    ''' （用户截图反馈"灰色文字显示不完整"）。这里压缩成「名字 · 端口」。
    ''' </summary>
    Private Function DshShortVersionText() As String
        Try
            Dim Ins As DshInstance = DshInstanceSelected
            If Ins Is Nothing Then Return ""
            Return $"{Ins.Name}　·　{Ins.Port}"
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 关闭 DSH 服务（红色按钮）。
    ''' 走 DshStop()，它会按"内存引用 → 按整合包目录/DSH_HOME → 只认启动器自己目录的扫描"
    ''' 三级兜底去找进程（见 ModDshLaunch 的说明），并在结束后复位运行状态。
    ''' </summary>
    Private Sub BtnCloseDsh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCloseDsh.Click
        If Not DshModeEnabled() Then Return
        '已在运行中的 dsh 可能有未保存的会话，先确认一次
        If MyMsgBox("是否关闭 DeepSeekHarness 服务？" & vbCrLf &
                    "正在这个 dsh 里进行的对话会被中断。",
                    "关闭 DSH", "关闭", "取消", IsWarn:=True) <> 1 Then Return
        RunInThread(
        Sub()
            Try
                DshStop()
            Catch ex As Exception
                Logger.Error(ex, "关闭 DSH 失败")
                RunInUi(Sub() Hint("关闭 DSH 失败：" & ex.Message, HintType.Red))
            End Try
            '进程状态变了，强制刷新按钮外观（DshBtnLastKey 里带了 DshIsRunning，会自动重算）
            RunInUi(
            Sub()
                Try
                    DshBtnLastKey = ""
                    RefreshButtonsUI()
                Catch ex As Exception
                    Logger.Warn(ex, "刷新启动按钮失败")
                End Try
            End Sub)
        End Sub)
    End Sub

    ''' <summary>按钮文字或宽度变化时，重新计算底部那行说明文字的左右边距，让它始终居中于按钮区域。</summary>
    Private Sub DshRefreshLabVersionMargin()
        Try
            Dim Extra As Double = 0
            If BtnCloseDsh.Visibility = Visibility.Visible Then Extra = BtnCloseDsh.ActualWidth + 10
            Dim M As Double = Math.Max(4, Extra / 2)
            If Math.Abs(LabVersion.Margin.Right - M) > 0.5 Then
                LabVersion.Margin = New Thickness(LabVersion.Margin.Left, LabVersion.Margin.Top, M, LabVersion.Margin.Bottom)
            End If
        Catch
        End Try
    End Sub

    Private Sub BtnLaunch_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles BtnLaunch.SizeChanged
        DshRefreshLabVersionMargin()
    End Sub

    Private Sub BtnCloseDsh_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles BtnCloseDsh.SizeChanged
        DshRefreshLabVersionMargin()
    End Sub

    Public Sub RefreshButtonsUI() Handles BtnLaunch.Loaded
        If Not BtnLaunch.IsLoaded Then Return
        If DshModeEnabled() Then
            RefreshDshButtonsUI()
            Return
        End If
        '以下为原版 Minecraft 模式
        '获取当前状态
        Dim CurrentState As Integer
        If (Not IsLoadFinished) OrElse McInstanceListLoader.State = LoadState.Loading OrElse McFolderListLoader.State = LoadState.Loading Then
            CurrentState = 0
        Else
            If McInstanceSelected Is Nothing Then
                If Settings.Get(Of Boolean)("UiHiddenPageDownload") AndAlso Not PageSetupUI.HiddenForceShow Then
                    CurrentState = 1
                Else
                    CurrentState = 2
                End If
            Else
                CurrentState = 3
            End If
        End If
        '更新状态
        If CurrentState = BtnLaunchState AndAlso
           If(McInstanceSelected Is Nothing, "", McInstanceSelected.PathVersion) = If(BtnLaunchInstance Is Nothing, "", BtnLaunchInstance.PathVersion) Then GoTo ExitRefresh
        BtnLaunchInstance = McInstanceSelected
        BtnLaunchState = CurrentState
        Select Case CurrentState
            Case 0
                Logger.Info("启动按钮：正在加载 Minecraft 版本")
                FrmLaunchLeft.BtnLaunch.Text = "正在加载"
                FrmLaunchLeft.BtnLaunch.IsEnabled = False
                FrmLaunchLeft.LabVersion.Text = "正在加载中，请稍候"
                FrmLaunchLeft.BtnVersion.IsEnabled = False
                FrmLaunchLeft.BtnMore.Visibility = Visibility.Collapsed
            Case 1
                Logger.Info("启动按钮：无 Minecraft 版本，下载已禁用")
                FrmLaunchLeft.BtnLaunch.Text = "启动游戏"
                FrmLaunchLeft.BtnLaunch.IsEnabled = False
                FrmLaunchLeft.LabVersion.Text = "未找到可用的游戏版本"
                FrmLaunchLeft.BtnVersion.IsEnabled = True
                FrmLaunchLeft.BtnMore.Visibility = Visibility.Collapsed
            Case 2
                Logger.Info("启动按钮：无 Minecraft 版本，要求下载")
                FrmLaunchLeft.BtnLaunch.Text = "下载游戏"
                FrmLaunchLeft.BtnLaunch.IsEnabled = True
                FrmLaunchLeft.LabVersion.Text = "未找到可用的游戏版本"
                FrmLaunchLeft.BtnVersion.IsEnabled = True
                FrmLaunchLeft.BtnMore.Visibility = Visibility.Collapsed
            Case 3
                Logger.Info($"启动按钮：Minecraft 版本：{McInstanceSelected.PathVersion}")
                FrmLaunchLeft.BtnLaunch.Text = "启动游戏"
                FrmLaunchLeft.BtnVersion.IsEnabled = True
                FrmLaunchLeft.BtnLaunch.IsEnabled = True
                FrmLaunchLeft.LabVersion.Text = McInstanceSelected.Name
                'FrmLaunchLeft.BtnMore.Visibility = Visibility.Visible '由功能隐藏设置修改
        End Select
ExitRefresh:
        '功能隐藏
        FrmLaunchLeft.BtnVersion.Visibility = If(Not PageSetupUI.HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenFunctionSelect"), Visibility.Collapsed, Visibility.Visible)
        If CurrentState = 3 Then
            FrmLaunchLeft.BtnMore.Visibility = FrmLaunchLeft.BtnVersion.Visibility
        End If
    End Sub
    '取消按钮
    Private Sub BtnCancel_Click() Handles BtnCancel.Click
        If McLaunchLoaderReal IsNot Nothing Then
            McLaunchLoaderReal.Cancel()
            McLaunchLog("已取消启动")
            Try
                If McLaunchWatcher IsNot Nothing Then
                    McLaunchWatcher.Kill()
                ElseIf McLaunchProcess IsNot Nothing Then
                    If Not McLaunchProcess.HasExited Then McLaunchProcess.Kill()
                End If
            Catch ex As Exception
                Logger.Error(ex, "取消启动结束进程失败", LogBehavior.Toast)
            End Try
        End If
    End Sub
    '版本设置按钮
    Private Sub BtnMore_Click() Handles BtnMore.Click
        If DshModeEnabled() Then
            'DSH 模式：进入整合包管理（插件 / 技能 / 设置）。
            '必须走 Setup 的子页面路由：DshManager 这个顶级页枚举值是 10，
            '而 FormMain.PageChange 会拿它当 PanTitleSelect.Children 的下标（顶部导航只有 5 个按钮），
            '直接 PageChange(PageType.DshManager) 会抛 ArgumentOutOfRangeException（实机踩过）。
            If FrmDshManager Is Nothing Then FrmDshManager = New PageDshManager
            FrmDshManager.LoadInstance(DshInstanceSelected)
            FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupManager)
            Return
        End If
        If McLaunchLoader.State = LoadState.Loading Then Return
        McInstanceSelected.Load()
        PageInstanceLeft.Instance = McInstanceSelected
        FrmMain.PageChange(FormMain.PageType.InstanceSetup, 0)
    End Sub
    ''' <summary>
    ''' 每 0.1s 执行一次，刷新启动的数据 UI 显示。
    ''' </summary>
    Public Sub LaunchingRefresh()
        Try
            If McLaunchLoaderReal.State = LoadState.Canceled Then Return
            '阶段状态获取
            Dim IsLaunched As Boolean = False '是否已经启动游戏，只是在等待窗口
            Try
                For Each Loader In McLaunchLoaderReal.GetLoaderList(False)
                    If Loader.State = LoadState.Loading OrElse Loader.State = LoadState.Waiting Then
                        LabLaunchingStage.Text = Loader.Name
                        IsLaunched = Loader.Name = "等待游戏窗口出现" OrElse Loader.Name = "结束处理"
                        Exit Try
                    End If
                Next
                LabLaunchingStage.Text = "已完成"
            Catch ex As Exception
                Logger.Warn(ex, "获取是否启动完成失败，可能是由于启动状态改变导致集合已修改")
                Return
            End Try
            LabLaunchingTitle.Text = If(IsLaunched OrElse McLaunchLoaderReal.State = LoadState.Finished,
                "已启动游戏",
                If(CurrentLaunchOptions.SaveBatch Is Nothing, "正在启动游戏", "正在导出启动脚本"))
            If AniIsRun("Launch State Page") Then IsLaunched = False '等待页面切换动画完成
            '更新进度
            Dim ActualProgress = McLaunchLoaderReal.Progress
            If ActualProgress >= ShowProgress Then ShowProgress += (ActualProgress - ShowProgress) * 0.1 + 0.0025 '向实际进度靠一点
            If ActualProgress <= ShowProgress Then ShowProgress = ActualProgress '原来或处理后变得比实际进度高，直接回退
            If IsLaunched Then ShowProgress = 1 '如果已经完成了，就不卖关子了
            LabLaunchingProgress.Text = (ShowProgress * 100).ToString("0.00") & " %"
            '更新下载速度
            Dim HasLaunchDownloader As Boolean = False
            Try
                If NetManager.Speed = 0 AndAlso LabLaunchingDownload.Visibility = Visibility.Collapsed Then
                    '可能只是在检查文件，没有实际下载
                    HasLaunchDownloader = False
                Else
                    For Each Loader In NetManager.Tasks
                        If Loader.RealParent IsNot Nothing AndAlso Loader.RealParent.Name = "Minecraft 启动" AndAlso Loader.State = LoadState.Loading Then HasLaunchDownloader = True
                    Next
                End If
            Catch ex As Exception
                Logger.Warn(ex, "获取 Minecraft 启动下载器失败，可能是因为启动被取消")
                HasLaunchDownloader = False
            End Try
            LabLaunchingDownload.Text = StringUtils.FormatByteSize(NetManager.Speed) & "/s"
            '进度改变动画
            Dim AnimList As New List(Of AniData) From {
                 AaGridLengthWidth(ProgressLaunchingFinished, ShowProgress - ProgressLaunchingFinished.Width.Value, 130,, New AniEaseOutFluent),
                 AaGridLengthWidth(ProgressLaunchingUnfinished, 1 - ShowProgress - ProgressLaunchingUnfinished.Width.Value, 130,, New AniEaseOutFluent)
            }
            If HasLaunchDownloader = (LabLaunchingDownload.Visibility = Visibility.Collapsed) Then 'IsDownloadStateChanged
                LabLaunchingDownload.Visibility = Visibility.Visible
                LabLaunchingDownloadLeft.Visibility = Visibility.Visible
                AnimList.AddRange({
                    AaOpacity(LabLaunchingDownload, If(HasLaunchDownloader, 1, 0) - LabLaunchingDownload.Opacity, 90),
                    AaOpacity(LabLaunchingDownloadLeft, If(HasLaunchDownloader, 0.5, 0) - LabLaunchingDownloadLeft.Opacity, 90),
                    AaCode(
                    Sub()
                        If Not HasLaunchDownloader Then
                            LabLaunchingDownload.Visibility = Visibility.Collapsed
                            LabLaunchingDownloadLeft.Visibility = Visibility.Collapsed
                        End If
                    End Sub, 110)
                })
            End If
            If (Not IsLaunched) = (LabLaunchingProgress.Visibility = Visibility.Collapsed) Then 'IsProgressStateChanged
                LabLaunchingProgress.Visibility = Visibility.Visible
                LabLaunchingProgressLeft.Visibility = Visibility.Visible
                If IsLaunched Then PanLaunchingHint.Visibility = Visibility.Visible
                AnimList.AddRange({
                    AaOpacity(LabLaunchingProgress, If(Not IsLaunched, 1, 0) - LabLaunchingProgress.Opacity, 90),
                    AaOpacity(LabLaunchingProgressLeft, If(Not IsLaunched, 0.5, 0) - LabLaunchingProgressLeft.Opacity, 90),
                    AaOpacity(PanLaunchingHint, If(IsLaunched, 1, 0) - PanLaunchingHint.Opacity, 90)
                })
            End If
            AniStart(AnimList, "Launching Progress")
        Catch ex As Exception
            Logger.Error(ex, "刷新启动信息失败")
        End Try
    End Sub
    Private ShowProgress As Double = 0
    '尺寸改变动画
    Private IsWidthAnimating As Boolean = False
    Private ActualUsedWidth As Double
    Private Sub PanLaunchingInfo_SizeChangedW(sender As Object, e As SizeChangedEventArgs) Handles PanLaunchingInfo.SizeChanged
        Dim DeltaWidth As Double = e.NewSize.Width - e.PreviousSize.Width
        If e.PreviousSize.Width = 0 OrElse IsWidthAnimating OrElse Math.Abs(DeltaWidth) < 1 OrElse PanLaunchingInfo.ActualWidth = 0 Then Return
        AniStart({
            AaWidth(PanLaunchingInfo, DeltaWidth, 180,, New AniEaseOutFluent),
            AaCode(Sub()
                       IsWidthAnimating = False
                       PanLaunchingInfo.Width = ActualUsedWidth
                   End Sub,, True)
        }, "Launching Info Width")
        IsWidthAnimating = True
        ActualUsedWidth = PanLaunchingInfo.Width
        PanLaunchingInfo.Width = e.PreviousSize.Width
    End Sub
    Private IsHeightAnimating As Boolean = False
    Private ActualUsedHeight As Double
    Private Sub PanLaunchingInfo_SizeChangedH(sender As Object, e As SizeChangedEventArgs) Handles PanLaunchingInfo.SizeChanged
        Dim DeltaHeight As Double = e.NewSize.Height - e.PreviousSize.Height
        If e.PreviousSize.Height = 0 OrElse IsHeightAnimating OrElse Math.Abs(DeltaHeight) < 1 OrElse PanLaunchingInfo.ActualHeight = 0 Then Return
        AniStart({
            AaHeight(PanLaunchingInfo, DeltaHeight, 180,, New AniEaseOutFluent),
            AaCode(Sub()
                       IsHeightAnimating = False
                       PanLaunchingInfo.Height = ActualUsedHeight
                   End Sub,, True)
        }, "Launching Info Height")
        IsHeightAnimating = True
        ActualUsedHeight = PanLaunchingInfo.Height
        PanLaunchingInfo.Height = e.PreviousSize.Height
    End Sub

End Class
