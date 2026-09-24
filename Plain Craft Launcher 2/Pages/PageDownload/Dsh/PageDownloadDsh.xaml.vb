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
    End Sub

    Private Sub Init() Handles Me.Loaded
        PanBack.ScrollToHome()
        LabRoot.Text = "当前版本仓库：" & DshVersionRoot
    End Sub

    ''' <summary>点击右上角刷新。</summary>
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

    Private Sub Version_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Item As MyListItem = TryCast(sender, MyListItem)
        If Item Is Nothing Then Return
        Dim Info As DshVersionInfo = TryCast(Item.Tag, DshVersionInfo)
        If Info Is Nothing Then Return

        '按钮文案（MyMsgBox 最多三个按钮）
        Dim Btn1 As String = If(Info.Installed, "重新安装", "安装")
        Dim Btn2 As String = If(DshInstanceSelected Is Nothing, "", "安装并绑定到「" & DshInstanceSelected.Name & "」")
        Dim Btn3 As String = If(Info.Installed, "卸载", "查看更新说明")
        If Btn2 = "" Then Btn2 = Btn3 : Btn3 = ""

        Dim Text As String = $"版本：{Info.Version}{vbCrLf}" &
                             $"发布时间：{Info.PublishTimeText}{vbCrLf}" &
                             $"类型：{If(Info.Channel = "alpha", "Alpha 测试版", If(Info.Channel = "rc", "RC 候选版", "正式版"))}{vbCrLf}" &
                             $"本地状态：{If(Info.Installed, "已安装", "未安装")}{vbCrLf}" &
                             $"npm：{If(Info.TarballUrl = "", "该版本未发布到 npm，无法安装", "可安装")}"
        If Info.Version = DshDefaultVersion Then Text &= vbCrLf & "（这是启动器内置的推荐版本）"

        Dim Choice As Integer = MyMsgBox(Text, "DSH " & Info.Version, Btn1, Btn2, Btn3)
        Dim Chosen As String = ""
        Select Case Choice
            Case 1 : Chosen = Btn1
            Case 2 : Chosen = Btn2
            Case 3 : Chosen = Btn3
        End Select
        If Chosen = "" Then Return

        If Chosen = Btn1 Then
            InstallVersion(Info, False)
        ElseIf Chosen = Btn2 Then
            If DshInstanceSelected Is Nothing Then
                InstallVersion(Info, False)
            Else
                InstallVersion(Info, True)
            End If
        ElseIf Chosen = Btn3 Then
            If Info.Installed Then
                RemoveVersion(Info)
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

        DshVersionInstallLoader.Start(Info.Version, IsForceRestart:=True)
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
