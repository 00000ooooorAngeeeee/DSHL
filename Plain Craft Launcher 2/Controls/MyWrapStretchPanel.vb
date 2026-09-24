Imports System.Windows
Imports System.Windows.Controls

''' <summary>
''' 自适应按钮行：**能一行放下就平分撑满，放不下就换行**。
'''
''' 为什么需要（用户反馈）：「整合包」卡片那行按钮原来是固定 5 列的 Grid，
''' 窗口一窄右边缘的「启动 DeepSeekHarness / 关闭 DSH」就被裁掉，看不到也点不到。
'''
''' 为什么不用 WPF 自带的 WrapPanel：它只负责换行，**不会把剩余宽度分给子项**，
''' 于是窄窗口下能换行、宽窗口下按钮却挤在左边、右边留一大片空白，和卡片里其他控件对不齐。
''' 这里补上"把剩余宽度平均分给各子项"的逻辑（子项自身的 Margin 会被算进去）。
'''
''' 用法：直接当容器用即可，不需要设任何属性。
'''   XAML 里要记得在 .vbproj 的 Compile Include 列表里登记本文件（PCL 用的是显式文件列表）。
''' </summary>
Public Class MyWrapStretchPanel
    Inherits Panel

    ''' <summary>换行后的行距。</summary>
    Public Property RowSpacing As Double = 0

    ''' <summary>
    ''' 上一次排布结果的文字描述（例如 "可用宽 900 → 1 行：[5]"）。
    ''' 给调用方写日志用：**窄窗口下的换行行为靠肉眼看截图不可靠**
    ''' （实测发现 PCL 在很窄且很高的窗口下整个页面都不渲染内容，截图是空白），
    ''' 有了这个就能从日志精确确认"每行放了几个、有没有超出可用宽度"。
    ''' </summary>
    Public ReadOnly Property LastLayoutText As String
        Get
            Return _LastLayoutText
        End Get
    End Property
    Private _LastLayoutText As String = "（尚未排布）"

    ''' <summary>计算每一行放哪些子项（Measure/Arrange 与日志共用同一套逻辑，避免两处不一致）。</summary>
    Private Function BuildRows(AvailW As Double) As List(Of List(Of UIElement))
        Dim Rows As New List(Of List(Of UIElement))
        Dim Cur As New List(Of UIElement)
        Dim RowW As Double = 0
        For Each Child As UIElement In InternalChildren
            Dim W As Double = Child.DesiredSize.Width
            If Cur.Count > 0 AndAlso RowW + W > AvailW Then
                Rows.Add(Cur)
                Cur = New List(Of UIElement)
                RowW = 0
            End If
            Cur.Add(Child)
            RowW += W
        Next
        If Cur.Count > 0 Then Rows.Add(Cur)
        Return Rows
    End Function

    Private Sub UpdateLayoutText(AvailW As Double, Rows As List(Of List(Of UIElement)))
        Try
            Dim Parts As New List(Of String)
            For Each Row As List(Of UIElement) In Rows
                Parts.Add(Row.Count.ToString())
            Next
            _LastLayoutText = $"可用宽 {Math.Round(AvailW)} → {Rows.Count} 行：[{String.Join(",", Parts)}]"
        Catch
        End Try
    End Sub

    Protected Overrides Function MeasureOverride(availableSize As Size) As Size
        Dim AvailW As Double = If(Double.IsInfinity(availableSize.Width), Double.MaxValue, availableSize.Width)
        Dim TotalH As Double = 0
        Dim RowW As Double = 0
        Dim RowH As Double = 0
        Dim MaxW As Double = 0
        For Each Child As UIElement In InternalChildren
            Child.Measure(New Size(Double.PositiveInfinity, Double.PositiveInfinity))
            Dim D As Size = Child.DesiredSize
            If RowW > 0 AndAlso RowW + D.Width > AvailW Then
                '换行
                TotalH += RowH + RowSpacing
                MaxW = Math.Max(MaxW, RowW)
                RowW = 0
                RowH = 0
            End If
            RowW += D.Width
            RowH = Math.Max(RowH, D.Height)
        Next
        TotalH += RowH
        MaxW = Math.Max(MaxW, RowW)
        '不要把期望宽度报成 MaxValue，否则外层容器会被撑爆
        If Double.IsInfinity(availableSize.Width) Then
            Return New Size(MaxW, TotalH)
        End If
        Return New Size(Math.Min(MaxW, availableSize.Width), TotalH)
    End Function

    Protected Overrides Function ArrangeOverride(finalSize As Size) As Size
        Dim AvailW As Double = finalSize.Width
        '1. 先算每一行放哪些子项
        Dim Rows As List(Of List(Of UIElement)) = BuildRows(AvailW)
        UpdateLayoutText(AvailW, Rows)

        '2. 逐行摆放；单行且需要拉伸时把剩余宽度平分
        Dim Y As Double = 0
        For Each Row As List(Of UIElement) In Rows
            Dim RowH As Double = 0
            Dim UsedW As Double = 0
            For Each Child As UIElement In Row
                RowH = Math.Max(RowH, Child.DesiredSize.Height)
                UsedW += Child.DesiredSize.Width
            Next
            '只有一行时才拉伸：多行说明每行都被塞满了，没有剩余宽度可分
            Dim CanStretch As Boolean = Rows.Count = 1
            Dim Extra As Double = If(CanStretch, Math.Max(0, AvailW - UsedW), 0)
            Dim Per As Double = If(Row.Count > 0, Extra / Row.Count, 0)
            Dim X As Double = 0
            For Each Child As UIElement In Row
                Dim W As Double = Child.DesiredSize.Width + Per
                Child.Arrange(New Rect(X, Y, W, RowH))
                X += W
            Next
            Y += RowH + RowSpacing
        Next
        Return New Size(AvailW, Y)
    End Function
End Class
