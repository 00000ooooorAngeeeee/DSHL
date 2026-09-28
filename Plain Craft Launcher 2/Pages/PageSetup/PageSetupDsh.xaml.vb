' ============================================================================================
'  PageSetupDsh —— 设置页的「DSH 运行环境」（需求 5）
'
'  可以在这里查看/修改：
'    · Node.js 运行环境位置（node.exe 所在目录）
'    · dsh 本体（版本仓库）位置
'    · npm 下载源
'    · 启动行为（自动开浏览器 / 退出时结束进程 / 遥测 / 默认版本）
'  并能一键下载 Node.js、跳到版本下载页、打开版本仓库目录。
' ============================================================================================
Public Class PageSetupDsh

    Private Sub PageSetupDsh_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()

        '重复加载部分
        Reload()

        '非重复加载部分
        Static IsLoaded As Boolean = False
        If IsLoaded Then Return
        IsLoaded = True

        AniControlEnabled += 1
        SettingService.RefreshSettings(Me)
        AniControlEnabled -= 1
    End Sub

    ''' <summary>
    ''' 刷新状态显示。
    ''' ★ 这里**绝不能**做耗时操作（用户反馈"从启动页切到设置页卡顿约 1 秒"）：
    '''   原来直接调 `DshNodeVersion()`，它会**起一个子进程**（node --version，
    '''   实测 200~1000ms），在 UI 线程上执行就是那 1 秒的卡顿。
    '''   → 改成：有缓存就立即用缓存显示（0ms），没缓存才去后台取并回调。
    '''   注意本方法是 `Handles Me.Loaded` 的重复加载路径，
    '''   每次进设置页都会跑，所以更不能用阻塞调用。
    ''' </summary>
    Public Sub Reload()
        'Node
        Dim Node As String = DshNodeExe
        If Node Is Nothing Then
            LabStatusNode.Text = "❌ 未检测到 Node.js —— 请点击下方的「下载 Node.js」自动安装，或手动填写 node.exe 所在目录。"
        Else
            Dim Ver As String = DshNodeVersionCached()
            If Ver IsNot Nothing Then
                LabStatusNode.Text = $"✔ Node.js 可用：{Ver}　（{Node}）"
            Else
                '缓存里还没有：先给占位，后台取到再更新（不阻塞界面）
                LabStatusNode.Text = $"✔ Node.js 可用　（{Node}）"
                DshNodeVersionAsync(
                    Sub(V As String)
                        Try
                            If V IsNot Nothing Then LabStatusNode.Text = $"✔ Node.js 可用：{V}　（{Node}）"
                        Catch
                        End Try
                    End Sub)
            End If
        End If

        'dsh 版本
        Dim Installed As List(Of String) = DshInstalledVersions()
        If Installed.Count = 0 Then
            LabStatusDsh.Text = "❌ 版本仓库中还没有任何 dsh 版本 —— 请到「下载 → DSH 版本」中安装。"
        Else
            LabStatusDsh.Text = $"✔ 已安装 {Installed.Count} 个 dsh 版本：{Installed.Take(6).Join("、")}{If(Installed.Count > 6, " 等", "")}"
        End If

        '整合包
        LabStatusRoot.Text = $"版本仓库：{DshVersionRoot}{vbCrLf}" &
                             $"整合包目录：{DshInstanceRoot}（共 {DshInstanceList.Count} 个）"
    End Sub

    ''' <summary>初始化本页设置。</summary>
    Public Sub Reset()
        If MyMsgBox("是否要初始化 DSH 运行环境页面的所有设置？该操作不可撤销。" & vbCrLf &
                    "（已安装的 dsh 版本与整合包不会被删除）", "初始化确认", , "取消", IsWarn:=True) <> 1 Then Return
        SettingService.ResetSettings(Me)
        Reload()
        Hint("已初始化 DSH 运行环境设置", HintType.Green)
    End Sub

#Region "按钮"

    '注意（实机踩坑）：PCL 的 MyButton.Click 委托是 MouseButtonEventArgs，**不是 EventArgs**。
    '写成 EventArgs 时页面构造就会抛 XamlParseException（"无法从文本 X 创建 Click"），
    '整个设置页打不开。PageDownloadDsh 里的 MyIconButton 处理函数是同一类约定。

    Private Sub InstallNode_Click(sender As Object, e As MouseButtonEventArgs)
        If MyMsgBox("将从 npmmirror 镜像下载 Node.js 到启动器目录下：" & vbCrLf &
                    DshRuntimeRoot & vbCrLf & vbCrLf &
                    "下载完成后会自动解压并接管，是否继续？", "下载 Node.js", "下载", "取消") <> 1 Then Return
        DshNodeInstallLoader.Start(0, IsForceRestart:=True)
    End Sub

    Private Sub OpenDownload_Click(sender As Object, e As MouseButtonEventArgs)
        FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadDsh)
    End Sub

    Private Sub OpenFolder_Click(sender As Object, e As MouseButtonEventArgs)
        Try
            DirectoryUtils.Create(DshVersionRoot)
            OpenExplorer(DshVersionRoot)
        Catch ex As Exception
            MyMsgBox(ex.Message, "打开失败", IsWarn:=True)
        End Try
    End Sub

#End Region

End Class
