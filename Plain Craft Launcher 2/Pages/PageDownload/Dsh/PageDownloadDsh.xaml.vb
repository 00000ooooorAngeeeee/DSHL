' ============================================================================================
'  PageDownloadDsh —— 下载页的「DSH 版本」分类（需求 3）
'
'  展示 DeepSeekHarness 的全部历史版本：
'    · 按 alpha / rc 分组（另有"其他版本"兜底 stable 之类）
'    · 组内按发布时间倒序
'    · 每个版本标注发布时间（本地时区，来源 GitHub Releases，失败回退 npm）
'    · 点击条目弹出操作菜单：安装 / 卸载 / 查看更新说明
' ============================================================================================
Public Class PageDownloadDsh
    Implements IRefreshable

    ''' <summary>条目图标（PCL 的 Logo 属性接受 SVG path 数据）。</summary>
    Public Const LogoDsh As String = "M512 64C264.6 64 64 264.6 64 512s200.6 448 448 448 448-200.6 448-448S759.4 64 512 64z m0 96c194.4 0 352 157.6 352 352S706.4 864 512 864 160 706.4 160 512 317.6 160 512 160z M512 320a64 64 0 1 0 0 128 64 64 0 0 0 0-128z m0 192a48 48 0 0 0-48 48v160a48 48 0 0 0 96 0V560a48 48 0 0 0-48-48z"

    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanContent, Nothing, DshVersionListLoader, AddressOf Load_OnFinish)
        '任务栏登记/清理由模块负责（安装是后台任务，页面可能被切走，见 DshInstallStateChanged）
        DshInstallInit()
        AddHandler DshVersionInstallLoader.OnStateChangedUi, AddressOf InstallStateChanged
    End Sub

    ''' <summary>
    ''' 安装任务状态变化 → 只负责给提示。
    '''
    ''' ★ 原来这里会显示一个**居中的"正在安装 dsh"遮罩浮层**，已按用户要求移除：
    '''   用户原话"既然右下角有任务管理了，那么中间这个弹窗可以去除了"。
    '''   进度、取消都已在右下角任务卡片里，居中遮罩属于重复信息，
    '''   而且它会挡住版本列表，安装时没法继续浏览别的版本。
    '''   → 现在：进行中完全静默（右下角任务卡片已经说明了），
    '''           失败才弹一个**非阻塞**的 Hint，保证错误信息不丢。
    ''' </summary>
    Private Sub InstallStateChanged(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
        Select Case NewState
            Case LoadState.Loading
                '不再显示居中遮罩；右下角任务管理器里有进度与取消
            Case LoadState.Failed
                Hint("安装 dsh 失败：" & If(Loader.Error?.GetDisplay(False), "未知错误"), HintType.Red)
            Case LoadState.Finished, LoadState.Canceled
                '无浮层需要收起
        End Select
    End Sub

    Private Sub Init() Handles Me.Loaded
        PanBack.ScrollToHome()
        LabRoot.Text = "当前版本仓库：" & DshVersionRoot
    End Sub

    ''' <summary>
    ''' 点击右上角刷新。
    ''' 注意（实机踩坑）：这个按钮是 MyIconButton，它的 Click 委托是 **EventArgs**；
    ''' 而 MyButton / MyListItem / MyLoading 的 Click 委托才是 MouseButtonEventArgs。
    ''' 两者写反都会在页面构造时抛 XamlParseException，表现是整个页面打不开。
    ''' </summary>
    Public Sub Refresh_Click(sender As Object, e As EventArgs)
        DshRefreshVersionList()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        DshRefreshVersionList()
    End Sub

    Private Sub Load_OnFinish()
        Dim Result As DshVersionListResult = DshVersionListLoader.Output
        LabRoot.Text = "当前版本仓库：" & DshVersionRoot
        If Result Is Nothing Then Return

        '来源与警告
        LabSource.Text = $"数据来源：{Result.SourceName}　·　共 {Result.Versions.Count} 个版本　·　时间为本地时区"
        If Result.Warnings.Any() Then
            HintWarning.Text = Result.Warnings.Join(vbCrLf)
            HintWarning.Visibility = Visibility.Visible
        Else
            HintWarning.Visibility = Visibility.Collapsed
        End If

        BuildGroup(PanAlpha, CardAlpha, Result.Where("alpha"), "alpha")
        BuildGroup(PanRelease, CardRelease, Result.Where("rc"), "rc")
        BuildGroup(PanOther, CardOther, Result.Where("stable"), "stable")
    End Sub

    ''' <summary>把一个分组渲染成列表。</summary>
    Private Sub BuildGroup(Panel As StackPanel, Card As MyCard, Versions As List(Of DshVersionInfo), Channel As String)
        Panel.Children.Clear()
        If Versions Is Nothing OrElse Versions.Count = 0 Then
            Card.Visibility = Visibility.Collapsed
            Return
        End If
        Card.Visibility = Visibility.Visible

        For Each Info As DshVersionInfo In Versions
            Dim Item As New MyListItem With {
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable,
                .Title = Info.Version,
                .Info = DshVersionDisplayText(Info),
                .Height = 42,
                .Logo = LogoDsh,
                .LogoScale = 0.8,
                .Tag = Info
            }
            AddHandler Item.Click, AddressOf Version_Click
            '右键菜单：把「卸载 / 查看更新说明 / 重新安装」这类次要且可能有破坏性的操作放这里，
            '避免左键弹窗按钮过多（之前因为按钮位不够，用户反馈过"没有取消选项"）。
            Item.ContextMenu = BuildVersionMenu(Info)
            Panel.Children.Add(Item)
        Next

        '底部说明
        Dim Tip As String = ""
        Select Case Channel
            Case "alpha"
                Tip = "Alpha 版是最新的开发版本，功能最新但最不稳定，官方可能随时改接口。"
            Case "rc"
                Tip = "RC（Release Candidate）版是候选发布版，相对稳定，建议日常使用。"
            Case Else
                Tip = "正式版（如果官方将来发布的话）。"
        End Select
        Panel.Children.Add(New TextBlock With {
            .Text = Tip, .Margin = New Thickness(13, 10, 13, 2), .Opacity = 0.55, .FontSize = 12, .TextWrapping = TextWrapping.Wrap
        })
    End Sub

#Region "版本条目操作"

    '★ 原来这里有一个 CancelInstall_Click（取消安装）。
    '  居中安装浮层被移除后，「取消安装」按钮没有了 —— 用户改从**右下角任务管理器**
    '  的卡片上取消（那里本来就有取消入口，见 ModDshInstall 的 LoaderTaskbar 登记）。
    '  所以这个处理器一并删掉，避免留下孤立代码。
    '  注意：取消组合加载器会连带取消子任务，子任务里会 taskkill 掉整棵 npm 进程树，
    '  这条逻辑在子任务里，不受本次改动影响。

    ''' <summary>
    ''' 为版本条目构造右键菜单（PCL 标准写法：用 XML 生成，再从命名元素上挂事件）。
    ''' </summary>
    Private Function BuildVersionMenu(Info As DshVersionInfo) As ContextMenu
        Try
            Dim Menu As ContextMenu = GetObjectFromXML(
                <ContextMenu xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:PCL;assembly=Plain Craft Launcher 2">
                    <local:MyMenuItem x:Name="ItemReinstall" Header="重新安装该版本" Padding="0,2,0,0"
                                      Icon="M512 64C264.6 64 64 264.6 64 512s200.6 448 448 448 448-200.6 448-448S759.4 64 512 64z m0 96c194.4 0 352 157.6 352 352S706.4 864 512 864 160 706.4 160 512 317.6 160 512 160z M512 320a64 64 0 1 0 0 128 64 64 0 0 0 0-128z m0 192a48 48 0 0 0-48 48v160a48 48 0 0 0 96 0V560a48 48 0 0 0-48-48z" />
                    <local:MyMenuItem x:Name="ItemNotes" Header="查看更新说明"
                                      Icon="F1 M 38,20.5833C 42.9908,20.5833 47.4912,22.6825 50.6667,26.046L 50.6667,17.4167L 55.4166,22.1667L 55.4167,34.8333L 42.75,34.8333L 38,30.0833L 46.8512,30.0833C 44.6768,27.6539 41.517,26.125 38,26.125C 31.9785,26.125 27.0037,30.6068 26.2296,36.4167L 20.6543,36.4167C 21.4543,27.5397 28.9148,20.5833 38,20.5833 Z" />
                    <local:MyMenuItem x:Name="ItemUninstall" Header="卸载该版本" Padding="0,0,0,2"
                                      Icon="F1 M 26.9166,22.1667L 37.9999,33.25L 49.0832,22.1668L 53.8332,26.9168L 42.7499,38L 53.8332,49.0834L 49.0833,53.8334L 37.9999,42.75L 26.9166,53.8334L 22.1666,49.0833L 33.25,38L 22.1667,26.9167L 26.9166,22.1667 Z" />
                </ContextMenu>)

            CType(Menu.FindName("ItemReinstall"), MyMenuItem).IsEnabled = (Info.TarballUrl <> "")
            CType(Menu.FindName("ItemNotes"), MyMenuItem).IsEnabled = (Info.ReleaseNotes <> "")
            CType(Menu.FindName("ItemUninstall"), MyMenuItem).IsEnabled = Info.Installed

            AddHandler CType(Menu.FindName("ItemReinstall"), MyMenuItem).Click,
                Sub() InstallVersion(Info, False)
            AddHandler CType(Menu.FindName("ItemNotes"), MyMenuItem).Click,
                Sub() MyMsgBox(If(Info.ReleaseNotes = "", "该版本没有提供更新说明。", Info.ReleaseNotes),
                               "DSH " & Info.Version & " 更新说明")
            AddHandler CType(Menu.FindName("ItemUninstall"), MyMenuItem).Click,
                Sub() RemoveVersion(Info)
            Return Menu
        Catch ex As Exception
            Logger.Warn(ex, "构造版本右键菜单失败")
            Return Nothing
        End Try
    End Function

    Private Sub Version_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Item As MyListItem = TryCast(sender, MyListItem)
        If Item Is Nothing Then Return
        Dim Info As DshVersionInfo = TryCast(Item.Tag, DshVersionInfo)
        If Info Is Nothing Then Return

        '按钮文案。MyMsgBox 最多三个按钮，所以这样分配：
        '   按钮1 = 安装 / 重新安装（主操作）
        '   按钮2 = 有整合包时是「安装并绑定到「X」」；否则是「查看更新说明」（有说明时）
        '   按钮3 = 取消（**必须始终有**，否则用户没有明确的退出口，反馈过这个问题）
        Dim BtnInstall As String = If(Info.Installed, "重新安装", "安装")
        Dim BtnSecondary As String = ""
        If DshInstanceSelected IsNot Nothing Then
            BtnSecondary = "安装并绑定到「" & DshInstanceSelected.Name & "」"
        ElseIf Info.ReleaseNotes <> "" Then
            BtnSecondary = "查看更新说明"
        End If
        Const BtnCancel As String = "取消"

        Dim Text As String = $"版本：{Info.Version}{vbCrLf}" &
                             $"发布时间：{Info.PublishTimeText}{vbCrLf}" &
                             $"类型：{If(Info.Channel = "alpha", "Alpha 测试版", If(Info.Channel = "rc", "RC 候选版", "正式版"))}{vbCrLf}" &
                             $"本地状态：{If(Info.Installed, "已安装", "未安装")}{vbCrLf}" &
                             $"npm：{If(Info.TarballUrl = "", "该版本未发布到 npm，无法安装", "可安装")}"
        If Info.Version = DshDefaultVersion Then Text &= vbCrLf & "（这是启动器内置的推荐版本）"
        If DshInstanceSelected Is Nothing Then
            Text &= vbCrLf & "（还没有选择整合包，所以只能安装到版本仓库；装好后可在整合包里绑定它）"
        End If

        Dim Choice As Integer = MyMsgBox(Text, "DSH " & Info.Version, BtnInstall, BtnSecondary, BtnCancel)
        If Choice <= 0 OrElse Choice > 3 Then Return

        '按钮 3 是取消（当 BtnSecondary 为空时，第三个按钮仍然是取消）
        If Choice = 3 Then
            If BtnSecondary = "" Then Return '取消
        End If

        If Choice = 1 Then
            InstallVersion(Info, False)
        ElseIf Choice = 2 AndAlso BtnSecondary <> "" Then
            If DshInstanceSelected IsNot Nothing Then
                InstallVersion(Info, True)
            Else
                MyMsgBox(If(Info.ReleaseNotes = "", "该版本没有提供更新说明。", Info.ReleaseNotes), "DSH " & Info.Version & " 更新说明")
            End If
        End If
    End Sub

    Private Sub InstallVersion(Info As DshVersionInfo, BindToInstance As Boolean)
        If Info.TarballUrl = "" Then
            Hint("该版本没有发布到 npm（只有 GitHub tag），无法通过 npm 安装", HintType.Red)
            Return
        End If
        If Not DshNodeReady() Then
            If MyMsgBox("还没有配置 Node.js 运行环境。" & vbCrLf & "是否现在去「设置 → DSH 运行环境」里一键下载？",
                        "需要 Node.js", "去看看", "取消") = 1 Then
                FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupDsh)
            End If
            Return
        End If
        If BindToInstance AndAlso DshInstanceSelected Is Nothing Then
            Hint("还没有选择整合包，无法绑定", HintType.Red)
            Return
        End If

        '安装完成后的动作
        InstallAfterAction = Nothing
        If BindToInstance Then
            Dim Target As DshInstance = DshInstanceSelected
            InstallAfterAction = Sub()
                                        Target.DshVersion = Info.Version
                                        DshWriteManifest(Target)
                                        Logger.Info($"整合包 {Target.Name} 已绑定 dsh {Info.Version}")
                                    End Sub
        End If

        '由组合统一启动安装任务。
        '不要在这里手动 Start 子任务：IsForceRestart:=True 对运行中的加载器也会返回 True，
        '会把任务重启一遍（同一次安装跑两遍 worker，界面报失败而 npm 在后台偷偷跑）。
        DshInstallStart(Info.Version)
        Hint($"正在安装 dsh {Info.Version}，请稍候……", HintType.Blue)
    End Sub

    Private Sub RemoveVersion(Info As DshVersionInfo)
        If MyMsgBox($"确定要卸载 dsh {Info.Version} 吗？" & vbCrLf & "该操作会把版本目录移到回收站。", "卸载版本",
                    "卸载", "取消", IsWarn:=True) <> 1 Then Return
        Try
            DshUninstallVersion(Info.Version)
            Hint($"已卸载 dsh {Info.Version}", HintType.Green)
            DshRefreshVersionList()
        Catch ex As Exception
            MyMsgBox(ex.Message, "卸载失败", IsWarn:=True)
        End Try
    End Sub

#End Region

End Class
