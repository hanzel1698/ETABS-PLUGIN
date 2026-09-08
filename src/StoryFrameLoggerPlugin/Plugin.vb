' =============================================================================
' StoryFrameLoggerPlugin.vb
'
' Proof-of-concept ETABS "External Plugin" that demonstrates the CSI OAPI
' plugin architecture end-to-end:
'   1. Implements the ETABSv1.cPlugin contract required by ETABS to load and
'      run a compiled DLL from the External Plugin dialog.
'   2. Reads basic data from whatever model is currently open (story names,
'      total frame count, and a column/beam breakdown).
'   3. Writes the results to a log file in the user's Documents folder and
'      shows them in a message box.
'   4. Fails soft: if no model is open, or ETABS hands back an unexpected
'      state, the plugin reports that instead of crashing.
'
' This class is the plugin's entry point. ETABS discovers it via reflection
' when the compiled DLL is added through File/Tools > External Plugin, so no
' COM registration step is required - only that the assembly is built against
' the same ETABSv1 interop referenced by the running ETABS instance.
' =============================================================================

Imports System
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports ETABSv1

Public Class StoryFrameLoggerPlugin
    Implements cPlugin

    ''' <summary>
    ''' Entry point ETABS calls when the user runs the plugin from the
    ''' External Plugin dialog. ISapPlugin is the live connection to the
    ''' running ETABS instance - from it we reach the open model (SapModel).
    ''' </summary>
    ''' <param name="ISapPlugin">Handle to the running ETABS application, supplied by ETABS itself.</param>
    ''' <param name="ret">0 on success, non-zero to signal an error back to ETABS.</param>
    Public Sub Main(ByRef ISapPlugin As cOAPI, ByRef ret As Integer) Implements cPlugin.Main
        ret = 0

        Try
            If ISapPlugin Is Nothing Then
                ShowAndLogResult("Plugin error: ETABS did not supply an application handle.")
                ret = 1
                Return
            End If

            Dim sapModel As cSapModel = ISapPlugin.SapModel
            If sapModel Is Nothing Then
                ShowAndLogResult("No model is currently open in ETABS. Open a model and run the plugin again.")
                ret = 1
                Return
            End If

            Dim report As New StringBuilder()
            report.AppendLine("=== ETABS Story/Frame Logger (POC) ===")
            report.AppendLine($"Run time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            report.AppendLine()

            AppendStorySummary(sapModel, report)
            AppendFrameSummary(sapModel, report)

            ShowAndLogResult(report.ToString())

        Catch ex As Exception
            ' Any unexpected COM/API failure lands here instead of crashing ETABS.
            ret = 1
            MessageBox.Show(
                "Story/Frame Logger failed: " & ex.Message,
                "Story/Frame Logger",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>
    ''' Text ETABS displays for this plugin in the External Plugin list.
    ''' </summary>
    Public Function Info(ByRef Text As String) As Integer Implements cPlugin.Info
        Text = "Story/Frame Logger (POC) - logs story names and column/beam counts for the open model"
        Info = 0
    End Function

    ''' <summary>Appends the model's story names to the report.</summary>
    Private Sub AppendStorySummary(sapModel As cSapModel, report As StringBuilder)
        Dim numStories As Integer = 0
        Dim storyNames() As String = Nothing
        Dim result As Integer = sapModel.Story.GetNameList(numStories, storyNames)

        If result <> 0 OrElse numStories = 0 OrElse storyNames Is Nothing Then
            report.AppendLine("Stories: none found.")
            report.AppendLine()
            Return
        End If

        report.AppendLine($"Stories ({numStories}):")
        For Each storyName As String In storyNames
            report.AppendLine("  - " & storyName)
        Next
        report.AppendLine()
    End Sub

    ''' <summary>
    ''' Appends total frame count plus a column/beam breakdown (via each
    ''' frame's design orientation) to the report.
    ''' </summary>
    Private Sub AppendFrameSummary(sapModel As cSapModel, report As StringBuilder)
        Dim numFrames As Integer = 0
        Dim frameNames() As String = Nothing
        Dim result As Integer = sapModel.FrameObj.GetNameList(numFrames, frameNames)

        If result <> 0 OrElse numFrames = 0 OrElse frameNames Is Nothing Then
            report.AppendLine("Frames: none found.")
            Return
        End If

        Dim columnCount As Integer = 0
        Dim beamCount As Integer = 0
        Dim otherCount As Integer = 0

        For Each frameName As String In frameNames
            ' Default (unset) value is intentionally left as the enum's zero
            ' member rather than a named constant - only Column/Beam are
            ' relied on below, so this stays correct regardless of how the
            ' remaining members (brace/null/etc.) are actually named.
            Dim orientation As eFrameDesignOrientation
            sapModel.FrameObj.GetDesignOrientation(frameName, orientation)

            Select Case orientation
                Case eFrameDesignOrientation.Column
                    columnCount += 1
                Case eFrameDesignOrientation.Beam
                    beamCount += 1
                Case Else
                    otherCount += 1
            End Select
        Next

        report.AppendLine($"Total frame objects: {numFrames}")
        report.AppendLine($"  Columns: {columnCount}")
        report.AppendLine($"  Beams:   {beamCount}")
        If otherCount > 0 Then
            report.AppendLine($"  Other (braces/unassigned): {otherCount}")
        End If
    End Sub

    ''' <summary>
    ''' Writes the report to a timestamped log file under
    ''' Documents\ETABS-Plugin-Logs and shows it in a message box. Falls back
    ''' to a message-box-only result if the log file cannot be written.
    ''' </summary>
    Private Sub ShowAndLogResult(message As String)
        Try
            Dim logDir As String = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ETABS-Plugin-Logs")
            Directory.CreateDirectory(logDir)

            Dim logPath As String = Path.Combine(
                logDir,
                $"StoryFrameLogger_{DateTime.Now:yyyyMMdd_HHmmss}.log")
            File.WriteAllText(logPath, message)

            MessageBox.Show(
                message & Environment.NewLine & Environment.NewLine & "Log saved to:" & Environment.NewLine & logPath,
                "Story/Frame Logger",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)
        Catch
            ' Logging is best-effort; always still show the result to the user.
            MessageBox.Show(message, "Story/Frame Logger", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Try
    End Sub

End Class
