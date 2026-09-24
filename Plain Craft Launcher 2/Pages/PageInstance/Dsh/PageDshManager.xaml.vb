' ============================================================================================
'  PageDshManager —— 整合包管理（需求 2、4）
'
'  一个页面搞定三件事：
'    · 整合包列表与选择（每个包 = 一个独立的 DSH_HOME + 独立的 dsh 版本）
'    · 该整合包的插件：列表 + 开关 + 安装/卸载（走 dsh plugin → pnpm）
'    · 该整合包的技能：列表 + 开关（改名实现，dsh 运行中也立即生效）
'
'  入口：启动页「版本设置」按钮、以及任何调用 FrmMain.PageChange(PageType.DshManager) 的地方。
' ============================================================================================
Public Class PageDshManager

    Private IsFirstLoad As Boolean = True
    Private IsRefreshing As Boolean = False

    ''' <summary>当前正在管理的整合包。</summary>
    Public Property Instance As DshInstance = Nothing

    Private Sub PageDshManager_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        If IsFirstLoad Then
            IsFirstLoad = False
            DshInstanceListLoader.Start(0)
            DshInstanceListLoader.WaitForExit()
        End If
        '优先使用外部指定的整合包
        If Instance Is Nothing Then Instance = DshInstanceSelected
        Reload()
    End Sub

    ''' <summary>
    ''' 记录按钮行的排布结果（诊断用）。
    ''' 用户反馈过"窗口窄时「整合包」卡片的按钮被裁切"，改成 MyWrapStretchPanel 后
    ''' 靠肉眼看截图验证并不可靠 —— 实测发现 PCL 在又窄又高的窗口下整个页面都不渲染内容（截图是空白），
    ''' 所以让面板把自己的排布结果吐到日志里，这样任何窗口尺寸下都能客观确认
    ''' "每行放了几个按钮、有没有超出可用宽度"。
    ''' </summary>
    Private Sub PanButtons_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles PanButtons.SizeChanged
        Try
            Logger.Info($"整合包按钮行排布：宽 {Math.Round(e.NewSize.Width)} 实际 {Math.Round(PanButtons.ActualWidth)}，" &
                        $"{If(PanButtons.LastLayoutText, "（无）")}（{If(PanButtons.LastLayoutText IsNot Nothing AndAlso PanButtons.LastLayoutText.Contains("1 行"), "未换行", "已换行")}）")
        Catch
        End Try
    End Sub

    ''' <summary>从外部打开某个整合包的管理页。</summary>
    Public Sub LoadInstance(Target As DshInstance)
        Instance = Target
        If Target IsNot Nothing Then DshInstanceSelected = Target
        Reload()
    End Sub

    Public Sub Refresh_Click(sender As Object, e As EventArgs)
        DshRefreshInstanceList()
        DshInstanceListLoader.WaitForExit()
        Reload()
    End Sub

#Region "刷新界面"

    Public Sub Reload()
        If IsRefreshing Then Return
        IsRefreshing = True
        AniControlEnabled += 1
        Try
            '整合包下拉框
            ComboInstance.Items.Clear()
            For Each Item As DshInstance In DshInstanceList
                ComboInstance.Items.Add(New MyComboBoxItem With {.Content = Item.Name, .Tag = Item})
            Next
            If DshInstanceList.Count = 0 Then
                ComboInstance.Items.Add(New MyComboBoxItem With {.Content = "（还没有整合包）", .IsEnabled = False})
                ComboInstance.SelectedIndex = 0
                Instance = Nothing
            Else
                Dim Index As Integer = 0
                If Instance IsNot Nothing Then
                    For i As Integer = 0 To DshInstanceList.Count - 1
                        If DshInstanceList(i).PathInstance = Instance.PathInstance Then Index = i
                    Next
                Else
                    Instance = DshInstanceList(0)
                End If
                ComboInstance.SelectedIndex = Index
            End If

            '信息与按钮
            If Instance Is Nothing Then
                LabInstanceInfo.Text = "还没有整合包。点「新建整合包」创建一个吧。"
                BtnDelete.IsEnabled = False
                BtnOpenFolder.IsEnabled = False
                BtnLaunch.IsEnabled = False
                BtnStop.IsEnabled = False
                BtnPluginAdd.IsEnabled = False
                BtnSkillOpen.IsEnabled = False
            Else
                Dim Running As Boolean = DshProcessAlive(Instance)
                LabInstanceInfo.Text =
                    $"名称：{Instance.Name}{vbCrLf}" &
                    $"dsh 版本：{Instance.DshVersion}　（{(If(Instance.IsVersionInstalled, "已安装", "未安装"))}）{vbCrLf}" &
                    $"端口：{Instance.Port}　状态：{(If(Running, "运行中", "未运行"))}{vbCrLf}" &
                    $"DSH_HOME：{Instance.PathDshHome}{vbCrLf}" &
                    $"工作区：{Instance.Workspace}"
                BtnDelete.IsEnabled = Not Running
                BtnOpenFolder.IsEnabled = True
                BtnLaunch.IsEnabled = Instance.CanLaunch
                BtnStop.IsEnabled = Running
                BtnPluginAdd.IsEnabled = Instance.IsVersionInstalled
                BtnSkillOpen.IsEnabled = True
            End If

            LoadPlugins()
            LoadSkills()
        Catch ex As Exception
            Logger.Error(ex, "刷新整合包管理页失败")
        Finally
            AniControlEnabled -= 1
            IsRefreshing = False
        End Try
    End Sub

    Private Sub LoadPlugins()
        PanPlugins.Children.Clear()
        If Instance Is Nothing Then
            LabPluginInfo.Text = ""
            Return
        End If
        If Not Instance.IsVersionInstalled Then
            LabPluginInfo.Text = $"请先安装 dsh {Instance.DshVersion}，然后才能管理插件。"
            Return
        End If

        '确保 profile 存在
        If Not FileUtils.Exists(DshProfileDir(Instance) & "package.json") Then
            Try
                DshEnsureProfile(Nothing, Instance)
            Catch ex As Exception
                Logger.Warn(ex, "初始化 profile 失败")
            End Try
        End If

        Dim Plugins As List(Of DshPlugin) = DshScanPlugins(Instance)
        Dim PluginOn As Integer = Plugins.Where(Function(Plg) Plg.Enabled).Count()
        Dim PluginOff As Integer = Plugins.Count - PluginOn
        LabPluginInfo.Text = $"共 {Plugins.Count} 个插件　·　profile: {Instance.Profile}　·　启用 {PluginOn} / 关闭 {PluginOff}"
        If Plugins.Count = 0 Then
            PanPlugins.Children.Add(New TextBlock With {
                .Text = "还没有安装任何插件。点右上角「安装插件…」按 npm 包名安装。",
                .Margin = New Thickness(13, 6, 13, 4), .Opacity = 0.6, .FontSize = 12, .TextWrapping = TextWrapping.Wrap
            })
            Return
        End If

        For Each Plugin As DshPlugin In Plugins
            Dim Item As New MyListItem With {
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.CheckBox,
                .Title = Plugin.PackageName,
                .Info = If(Plugin.Version <> "", "v" & Plugin.Version & "　", "") &
                        If(Plugin.Installed, "", "（未安装到本地）") &
                        If(Plugin.Description <> "", "　" & Plugin.Description, ""),
                .Height = 42,
                .Checked = Plugin.Enabled,
                .Tag = Plugin
            }
            AddHandler Item.Changed, AddressOf Plugin_Changed
            PanPlugins.Children.Add(Item)
        Next
        PanPlugins.Children.Add(New TextBlock With {
            .Text = "插件开关通过 profile 的 cordis.patch.yml 生效；改动后需要重启该整合包的 dsh 才会完全生效。",
            .Margin = New Thickness(13, 10, 13, 0), .Opacity = 0.55, .FontSize = 12, .TextWrapping = TextWrapping.Wrap
        })
    End Sub

    Private Sub LoadSkills()
        PanSkills.Children.Clear()
        If Instance Is Nothing Then
            LabSkillInfo.Text = ""
            Return
        End If
        Dim Skills As List(Of DshSkill) = DshScanSkills(Instance)
        Dim SkillOn As Integer = Skills.Where(Function(Skl) Skl.Enabled).Count()
        Dim SkillOff As Integer = Skills.Count - SkillOn
        LabSkillInfo.Text = $"共 {Skills.Count} 个技能　·　启用 {SkillOn} / 关闭 {SkillOff}"
        If Skills.Count = 0 Then
            PanSkills.Children.Add(New TextBlock With {
                .Text = "这个整合包里还没有技能。把技能文件夹放进 " & DshSkillRoot(Instance) & " 即可；" & vbCrLf &
                        "也可以在新建整合包时选择「导入现有 DSH_HOME」。",
                .Margin = New Thickness(13, 6, 13, 4), .Opacity = 0.6, .FontSize = 12, .TextWrapping = TextWrapping.Wrap
            })
            Return
        End If

        For Each Skill As DshSkill In Skills
            Dim Info As String = If(Skill.Description <> "", Skill.Description, "")
            If Skill.WhenToUse <> "" Then Info &= If(Info = "", "", "　·　") & "何时使用：" & Skill.WhenToUse
            If Skill.Warning <> "" Then Info &= If(Info = "", "", "　·　") & "⚠ " & Skill.Warning
            Dim Item As New MyListItem With {
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.CheckBox,
                .Title = Skill.Name,
                .Info = Info,
                .Height = 42,
                .Checked = Skill.Enabled,
                .Tag = Skill
            }
            AddHandler Item.Changed, AddressOf Skill_Changed
            PanSkills.Children.Add(Item)
        Next
    End Sub

#End Region

#Region "事件"

    '约定（实机踩坑）：PCL 的 MyButton / MyIconButton / MyListItem 的 Click 委托都是 MouseButtonEventArgs，
    '不是 EventArgs 也不是 MouseEventArgs。写错会在页面构造时抛 XamlParseException
    '（"无法从文本 X 创建 Click"），表现为整个页面打不开。
    '对照：MyListItem.Changed 是 RouteEventArgs；MyComboBox 用标准 SelectionChangedEventArgs。
    Private Sub ComboInstance_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If IsRefreshing Then Return
        Dim Item As MyComboBoxItem = TryCast(ComboInstance.SelectedItem, MyComboBoxItem)
        If Item Is Nothing Then Return
        Dim Target As DshInstance = TryCast(Item.Tag, DshInstance)
        If Target Is Nothing Then Return
        Instance = Target
        DshInstanceSelected = Target
        Reload()
    End Sub

    Private Sub Plugin_Changed(sender As Object, e As RouteEventArgs)
        Dim Item As MyListItem = TryCast(sender, MyListItem)
        If Item Is Nothing OrElse Instance Is Nothing Then Return
        Dim Plugin As DshPlugin = TryCast(Item.Tag, DshPlugin)
        If Plugin Is Nothing Then Return
        Try
            DshSetPluginEnabled(Instance, Plugin, Item.Checked)
            Hint($"插件 {Plugin.PackageName} 已{(If(Item.Checked, "启用", "关闭"))}", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, $"切换插件状态失败：{Plugin.PackageName}")
            MyMsgBox(ex.Message, "切换插件状态失败", IsWarn:=True)
            Item.Checked = Plugin.Enabled
        End Try
    End Sub

    Private Sub Skill_Changed(sender As Object, e As RouteEventArgs)
        Dim Item As MyListItem = TryCast(sender, MyListItem)
        If Item Is Nothing OrElse Instance Is Nothing Then Return
        Dim Skill As DshSkill = TryCast(Item.Tag, DshSkill)
        If Skill Is Nothing Then Return
        Try
            DshSetSkillEnabled(Instance, Skill, Item.Checked)
            Hint($"技能 {Skill.Name} 已{(If(Item.Checked, "启用", "关闭"))}", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, $"切换技能状态失败：{Skill.Name}")
            MyMsgBox(ex.Message, "切换技能状态失败", IsWarn:=True)
            Item.Checked = Skill.Enabled
        End Try
    End Sub

    Private Sub New_Click(sender As Object, e As MouseButtonEventArgs)
        DshNewInstanceWizard()
        DshInstanceListLoader.WaitForExit()
        If Instance IsNot Nothing Then
            Dim Refreshed As DshInstance = DshInstanceList.FirstOrDefault(Function(i) i.Name = Instance.Name)
            If Refreshed IsNot Nothing Then Instance = Refreshed
        End If
        Reload()
    End Sub

    Private Sub Delete_Click(sender As Object, e As MouseButtonEventArgs)
        If Instance Is Nothing Then Return
        If MyMsgBox($"确定要删除整合包「{Instance.Name}」吗？" & vbCrLf & vbCrLf &
                    "它的 DSH_HOME（技能、插件、配置、会话）与工作区都会一并删除，且会移到回收站。" & vbCrLf &
                    "dsh 本体（版本仓库）不会被删除。", "删除整合包", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Try
            DshDeleteInstance(Instance)
            Instance = Nothing
            DshRefreshInstanceList()
            DshInstanceListLoader.WaitForExit()
            Reload()
            Hint("整合包已删除（可在回收站找回）", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "删除整合包失败")
            MyMsgBox(ex.Message, "删除失败", IsWarn:=True)
        End Try
    End Sub

    Private Sub OpenFolder_Click(sender As Object, e As MouseButtonEventArgs)
        If Instance Is Nothing Then Return
        Try
            DirectoryUtils.Create(Instance.PathInstance)
            OpenExplorer(Instance.PathInstance)
        Catch ex As Exception
            MyMsgBox(ex.Message, "打开失败", IsWarn:=True)
        End Try
    End Sub

    Private Sub Launch_Click(sender As Object, e As MouseButtonEventArgs)
        If Instance Is Nothing Then Return
        If Not DshNodeReady() Then
            If MyMsgBox("还没有配置 Node.js 运行环境，是否现在去设置里下载？", "需要 Node.js", "去看看", "取消") = 1 Then
                FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupDsh)
            End If
            Return
        End If
        If Not Instance.IsVersionInstalled Then
            If MyMsgBox($"整合包绑定的 dsh {Instance.DshVersion} 还没有安装，去下载页安装吗？", "需要安装 dsh", "去下载", "取消") = 1 Then
                FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadDsh)
            End If
            Return
        End If
        DshLaunchStart(Instance)
        RunInThread(
        Sub()
            DshLaunchLoader.WaitForExit()
            RunInUi(Sub() Reload())
        End Sub)
    End Sub

    Private Sub Stop_Click(sender As Object, e As MouseButtonEventArgs)
        DshStop()
        Reload()
    End Sub

    ''' <summary>
    ''' 安装插件。原来是"直接让你手输 npm 包名"，用户反馈**包名很难找**，
    ''' 所以改成先给三条路：
    '''   ① 搜索（按关键词搜 npm registry，列出候选让你挑）
    '''   ② 推荐（一键装 dshmarket —— 可视化插件市场，装上后在 dsh 界面里逛，彻底不用记包名）
    '''   ③ 手动输入（已经知道包名的情况）
    ''' 三条路最终都汇到 InstallPluginByName，保证行为一致。
    ''' </summary>
    Private Sub PluginAdd_Click(sender As Object, e As MouseButtonEventArgs)
        If Instance Is Nothing Then Return
        If Not Instance.IsVersionInstalled Then
            MyMsgBox($"请先安装该整合包绑定的 dsh {Instance.DshVersion}，然后才能管理插件。", "还不能装插件", IsWarn:=True)
            Return
        End If
        Dim Choice As Integer? = MyMsgBoxSelect(
            New ObjectModel.Collection(Of IMyRadio) From {
                New MyRadioBox With {.Text = "搜索插件（按关键词搜 npm，然后从结果里挑）"},
                New MyRadioBox With {.Text = "安装插件市场 dshmarket（推荐：装完在 dsh 里逛市场，不用记包名）"},
                New MyRadioBox With {.Text = "手动输入包名（我已经知道包名）"}
            }, "安装插件", "下一步", "取消")
        If Choice Is Nothing Then Return
        Select Case Choice.Value
            Case 0 : SearchAndInstallPlugin()
            Case 1 : InstallPluginByName("dshmarket")
            Case 2 : ManualInstallPlugin()
        End Select
    End Sub

    ''' <summary>路径③：手动输入包名。</summary>
    Private Sub ManualInstallPlugin()
        Dim Name As String = MyMsgBoxInput("安装插件", "输入 npm 包名（例如 dshmarket）。" & vbCrLf &
                                           "会通过 dsh plugin 装到这个整合包的 profile 里，需要联网。", "",
                                           New ObjectModel.Collection(Of Validate) From {New ValidateFunc(Function(v As String) If(String.IsNullOrWhiteSpace(v), "不能为空", Nothing))})
        If String.IsNullOrWhiteSpace(Name) Then Return
        InstallPluginByName(Name.Trim())
    End Sub

    ''' <summary>路径①：按关键词搜索 npm，再从结果里挑一个安装。</summary>
    Private Sub SearchAndInstallPlugin()
        Dim Keyword As String = MyMsgBoxInput("搜索插件", "输入关键词（例如 dsh market、dsh plugin、deepseek）。" & vbCrLf &
                                             "会在 npm registry 里搜索，然后从结果里挑。", "",
                                             New ObjectModel.Collection(Of Validate) From {New ValidateFunc(Function(v As String) If(String.IsNullOrWhiteSpace(v), "不能为空", Nothing))})
        If String.IsNullOrWhiteSpace(Keyword) Then Return
        Keyword = Keyword.Trim()
        Hint($"正在搜索：{Keyword}", HintType.Blue)

        RunInThread(
        Sub()
            Dim Items As List(Of DshPluginSearchItem) = Nothing
            Dim Err As String = ""
            Try
                Items = DshSearchPlugins(Keyword)
            Catch ex As Exception
                Err = ex.Message
                Logger.Warn(ex, $"搜索插件失败：{Keyword}")
            End Try
            RunInUi(
            Sub()
                Try
                    If Items Is Nothing OrElse Items.Count = 0 Then
                        '搜索不通或没结果时，给推荐清单兜底，别让用户卡住
                        Dim Tip As String = If(Err <> "", "搜索失败：" & Err & vbCrLf & vbCrLf, "没有搜到结果。" & vbCrLf & vbCrLf)
                        Dim Fallback As Integer? = MyMsgBoxSelect(
                            New ObjectModel.Collection(Of IMyRadio) From {
                                New MyRadioBox With {.Text = "dshmarket —— 可视化插件市场（推荐）"},
                                New MyRadioBox With {.Text = "改成手动输入包名"}
                            }, Tip & "要改成下面哪种方式？", "确定", "取消")
                        If Fallback Is Nothing Then Return
                        If Fallback.Value = 0 Then InstallPluginByName("dshmarket") Else ManualInstallPlugin()
                        Return
                    End If
                    Dim Controls As New ObjectModel.Collection(Of IMyRadio)
                    For Each It As DshPluginSearchItem In Items.Take(20)
                        Dim Desc As String = If(It.Description, "").Trim()
                        If Desc.Length > 60 Then Desc = Desc.Substring(0, 60) & "…"
                        Dim Mark As String = If(It.LooksLikePlugin, "【像 dsh 插件】", "")
                        Controls.Add(New MyRadioBox With {
                            .Text = $"{It.PackageName}  ({It.Version}) {Mark}" & If(Desc = "", "", "　" & Desc),
                            .Tag = It
                        })
                    Next
                    Dim Picked As Integer? = MyMsgBoxSelect(Controls, $"搜索结果（共 {Items.Count} 个，最多显示 20）", "安装这个", "取消")
                    If Picked Is Nothing Then Return
                    Dim Box As MyRadioBox = TryCast(Controls(Picked.Value), MyRadioBox)
                    If Box Is Nothing Then Return
                    Dim Chosen As DshPluginSearchItem = TryCast(Box.Tag, DshPluginSearchItem)
                    If Chosen Is Nothing Then Return
                    InstallPluginByName(Chosen.PackageName)
                Catch ex As Exception
                    Logger.Error(ex, "处理搜索结果失败")
                End Try
            End Sub)
        End Sub)
    End Sub

    ''' <summary>三条路径共用的实际安装动作。</summary>
    Private Sub InstallPluginByName(Name As String)
        If String.IsNullOrWhiteSpace(Name) Then Return
        Name = Name.Trim()
        RunInThread(
        Sub()
            Try
                DshInstallPlugin(Nothing, Instance, Name)
                RunInUi(Sub()
                            Hint($"插件 {Name} 安装完成", HintType.Green)
                            Reload()
                        End Sub)
            Catch ex As Exception
                Logger.Error(ex, $"安装插件失败：{Name}")
                RunInUi(Sub() MyMsgBox(ex.Message, "安装插件失败", IsWarn:=True))
            End Try
        End Sub)
    End Sub

    Private Sub SkillOpen_Click(sender As Object, e As MouseButtonEventArgs)
        If Instance Is Nothing Then Return
        Try
            DirectoryUtils.Create(DshSkillRoot(Instance))
            OpenExplorer(DshSkillRoot(Instance))
        Catch ex As Exception
            MyMsgBox(ex.Message, "打开失败", IsWarn:=True)
        End Try
    End Sub

#End Region

End Class
