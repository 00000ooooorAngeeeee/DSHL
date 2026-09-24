Public Class PageDownloadLeft
    Implements IRefreshable

#Region "页面切换"

    ''' <summary>
    ''' 当前页面的编号。
    ''' 注意（DSH 魔改）：主页面枚举值会被 FormMain.PageChange 当作
    ''' PanTitleSelect.Children 的下标用（顶部导航），而**子页面**枚举值会被当作
    ''' 本页 PanItem.Children 的下标用。所以隐藏条目时**只能改 Visibility、
    ''' 不能从 PanItem 里移除元素**，否则下标整体错位。
    ''' </summary>
    Public PageID As FormMain.PageSubType = FormMain.PageSubType.DownloadDsh

    ''' <summary>
    ''' DSH 模式：隐藏所有 Minecraft 相关条目，只留「DSH 版本」。
    '''
    ''' 为什么保留元素而不删除：本类与 FormMain 都按 Children 下标取控件
    ''' （CType(FrmDownloadLeft.PanItem.Children(SubType), MyListItem)），
    ''' 删掉元素会让 DownloadMod=2 之类的下标全部错位，直接取到错的控件。
    ''' 把「社区资源」这个分组标题的整行也一起隐藏（Visibility=Collapsed 只是不渲染，
    ''' 不再占位，所以视觉上就是干净的一页）。
    ''' </summary>
    Private Sub ApplyDshModeVisibility()
        If Not PageLaunchLeft.DshModeEnabled() Then Return
        For Each Child As Object In PanItem.Children
            If TypeOf Child Is TextBlock Then
                '「社区资源」分组标题（以及任何分组标题）
                CType(Child, TextBlock).Visibility = Visibility.Collapsed
            ElseIf TypeOf Child Is MyListItem Then
                If Child Is ItemDsh Then
                    Child.Visibility = Visibility.Visible
                Else
                    Child.Visibility = Visibility.Collapsed
                End If
            End If
        Next
        Logger.Info("DSH 模式：下载页只保留「DSH 版本」")
    End Sub

    ''' <summary>
    ''' 本控件加载后应用一次 DSH 模式的显隐，并补上"初始选中项"。
    '''
    ''' ★ 为什么必须补选中（用户实报的视觉 bug）：
    '''   XAML 里第一个条目 `ItemInstall`（原版游戏）带着 `Checked="True"`，
    '''   而 DSH 模式下它被上面那个方法设成 `Collapsed` ——
    '''   结果就是**默认选中项是个看不见的条目**，界面上表现为
    '''   "进了「下载 → DSH 版本」但左栏没有任何一项显示选中"，
    '''   点一下才通过 PageCheck 正常选中。
    '''   这与设置页一样：进入哪个子页面，就让哪一项显示为选中。
    '''   DSH 模式的下载页只有「DSH 版本」一项，所以这里直接选它。
    ''' </summary>
    Private Sub PageDownloadLeft_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ApplyDshModeVisibility()
        If PageLaunchLeft.DshModeEnabled() Then
            'PageID 的默认值就是 DownloadDsh，所以这里只补视觉状态，不触发页面切换
            '（PageCheck 里 `If sender.Tag IsNot Nothing` 之后会走 PageChange(8)，
            ' 而 PageChange 开头有 `If PageID = ID Then Return`，不会重复切页）
            ItemDsh.Checked = True
            Logger.Info("DSH 模式：下载页初始选中「DSH 版本」")
        End If
    End Sub

    ''' <summary>勾选事件改变页面。</summary>
    Private Sub PageCheck(sender As FrameworkElement, e As RouteEventArgs) Handles ItemInstall.Check, ItemMod.Check, ItemPack.Check, ItemResourcePack.Check, ItemShader.Check, ItemDataPack.Check, ItemDsh.Check
        '尚未初始化控件属性时，sender.Tag 为 Nothing，会导致切换到页面 0
        '若使用 IsLoaded，则会导致模拟点击不被执行（模拟点击切换页面时，控件的 IsLoaded 为 False）
        If sender.Tag IsNot Nothing Then PageChange(Val(sender.Tag))
    End Sub

    Public Function PageGet(Optional ID As FormMain.PageSubType = -1)
        If ID = -1 Then ID = PageID
        Select Case ID
            Case FormMain.PageSubType.DownloadInstall
                If FrmDownloadInstall Is Nothing Then FrmDownloadInstall = New PageDownloadInstall
                Return FrmDownloadInstall
            Case FormMain.PageSubType.DownloadMod
                If FrmDownloadMod Is Nothing Then FrmDownloadMod = New PageDownloadMod
                Return FrmDownloadMod
            Case FormMain.PageSubType.DownloadPack
                If FrmDownloadPack Is Nothing Then FrmDownloadPack = New PageDownloadPack
                Return FrmDownloadPack
            Case FormMain.PageSubType.DownloadResourcePack
                If FrmDownloadResourcePack Is Nothing Then FrmDownloadResourcePack = New PageDownloadResourcePack
                Return FrmDownloadResourcePack
            Case FormMain.PageSubType.DownloadShader
                If FrmDownloadShader Is Nothing Then FrmDownloadShader = New PageDownloadShader
                Return FrmDownloadShader
            Case FormMain.PageSubType.DownloadDataPack
                If FrmDownloadDataPack Is Nothing Then FrmDownloadDataPack = New PageDownloadDataPack
                Return FrmDownloadDataPack
            Case FormMain.PageSubType.DownloadDsh
                If FrmDownloadDsh Is Nothing Then FrmDownloadDsh = New PageDownloadDsh
                Return FrmDownloadDsh
            Case Else
                Throw New Exception("未知的下载子页面种类：" & ID)
        End Select
    End Function

    ''' <summary>
    ''' 切换现有页面。
    ''' </summary>
    Public Sub PageChange(ID As FormMain.PageSubType)
        If PageID = ID Then Return
        AniControlEnabled += 1
        Try
            PageChangeRun(PageGet(ID))
            PageID = ID
        Catch ex As Exception
            Logger.Error(ex, $"切换分页面失败（ID {ID}）")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub
    Private Shared Sub PageChangeRun(Target As MyPageRight)
        AniStop("FrmMain PageChangeRight") '停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        If Target.Parent IsNot Nothing Then Target.SetValue(ContentPresenter.ContentProperty, Nothing)
        FrmMain.PageRight = Target
        CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnExit()
        AniStart({
            AaCode(
            Sub()
                CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnForceExit()
                FrmMain.PanMainRight.Child = FrmMain.PageRight
                FrmMain.PageRight.Opacity = 0
            End Sub, 130),
            AaCode(
            Sub()
                '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                FrmMain.PageRight.Opacity = 1
                FrmMain.PageRight.PageOnEnter()
            End Sub, 30, True)
        }, "PageLeft PageChange")
    End Sub

#End Region

    '强制刷新
    Public Sub Refresh_Click(sender As Object, e As EventArgs) '由边栏按钮匿名调用
        Refresh(Val(sender.Tag))
    End Sub
    Public Sub Refresh() Implements IRefreshable.Refresh
        Refresh(FrmMain.PageCurrentSub)
    End Sub
    Public Sub Refresh(SubType As FormMain.PageSubType)
        ResourceProject.Cache.Clear()
        ResourceVersion.ProjectFilesCache.Clear()
        Select Case SubType
            Case FormMain.PageSubType.DownloadInstall
                DlClientListLoader.Start(IsForceRestart:=True)
                DlOptiFineListLoader.Start(IsForceRestart:=True)
                DlForgeListLoader.Start(IsForceRestart:=True)
                DlNeoForgeListLoader.Start(IsForceRestart:=True)
                DlLiteLoaderListLoader.Start(IsForceRestart:=True)
                DlFabricListLoader.Start(IsForceRestart:=True)
                DlFabricApiLoader.Start(IsForceRestart:=True)
                DlOptiFabricLoader.Start(IsForceRestart:=True)
                ItemInstall.Checked = True
            Case FormMain.PageSubType.DownloadMod
                If FrmDownloadMod IsNot Nothing Then
                    FrmDownloadMod.Content.Storage = New ResourceSearcher.SearchResult
                    FrmDownloadMod.Content.Page = 0
                    FrmDownloadMod.PageLoaderRestart()
                End If
                ItemMod.Checked = True
            Case FormMain.PageSubType.DownloadPack
                If FrmDownloadPack IsNot Nothing Then
                    FrmDownloadPack.Content.Storage = New ResourceSearcher.SearchResult
                    FrmDownloadPack.Content.Page = 0
                    FrmDownloadPack.PageLoaderRestart()
                End If
                ItemPack.Checked = True
            Case FormMain.PageSubType.DownloadResourcePack
                If FrmDownloadResourcePack IsNot Nothing Then
                    FrmDownloadResourcePack.Content.Storage = New ResourceSearcher.SearchResult
                    FrmDownloadResourcePack.Content.Page = 0
                    FrmDownloadResourcePack.PageLoaderRestart()
                End If
                ItemResourcePack.Checked = True
            Case FormMain.PageSubType.DownloadShader
                If FrmDownloadShader IsNot Nothing Then
                    FrmDownloadShader.Content.Storage = New ResourceSearcher.SearchResult
                    FrmDownloadShader.Content.Page = 0
                    FrmDownloadShader.PageLoaderRestart()
                End If
                ItemShader.Checked = True
            Case FormMain.PageSubType.DownloadDataPack
                If FrmDownloadDataPack IsNot Nothing Then
                    FrmDownloadDataPack.Content.Storage = New ResourceSearcher.SearchResult
                    FrmDownloadDataPack.Content.Page = 0
                    FrmDownloadDataPack.PageLoaderRestart()
                End If
                ItemDataPack.Checked = True
            Case FormMain.PageSubType.DownloadDsh
                DshRefreshVersionList()
                ItemDsh.Checked = True
        End Select
        Hint("正在刷新……", Log:=False)
    End Sub

    '点击返回
    Private Sub ItemInstall_Click(sender As Object, e As MouseButtonEventArgs) Handles ItemInstall.Click
        If Not ItemInstall.Checked Then Return
        FrmDownloadInstall.ExitSelectPage()
    End Sub

End Class
