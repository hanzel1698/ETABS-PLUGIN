' =============================================================================
' StoryFrameLoggerPlugin.vb
'
' Proof-of-concept ETABS "External Plugin" that demonstrates the CSI OAPI
' plugin architecture end-to-end:
'   1. Implements the ETABSv1.cPluginContract contract required by ETABS to
'      load and run a compiled DLL from the External Plugin dialog.
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
'
' NOTE: the plugin contract shape here (cPluginContract.Main taking the open
' cSapModel directly, plus a cPluginCallback used to signal completion via
' Finish()) was confirmed by reflecting over an installed ETABSv1.dll; it
' differs from the older cPlugin/cOAPI shape described in some CSI examples.
'
' NOTE: ETABS' loader resolves the plugin type by a hard-coded name -
' "<RootNamespace>.cPlugin" - rather than scanning the assembly for whatever
' implements cPluginContract. The class below MUST be named exactly cPlugin
' (confirmed by an "Value cannot be null. (Parameter 'type')" failure in
' ETABS when it wasn't) even though it implements the cPluginContract
' interface, not a type literally called cPlugin.
' =============================================================================

Imports System
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports ETABSv1

Public Class cPlugin
    Implements cPluginContract

    ''' <summary>
    ''' Entry point ETABS calls when the user runs the plugin from the
    ''' External Plugin dialog. SapModel is the currently open model (may be
    ''' Nothing if no model is open); ISapPlugin is a callback handle used to
    ''' tell ETABS the plugin is done via Finish(0 for success, non-zero for
    ''' an error).
    ''' </summary>
    ''' <param name="SapModel">The currently open model, supplied by ETABS.</param>
    ''' <param name="ISapPlugin">Callback used to signal completion back to ETABS.</param>
    Public Sub Main(ByRef SapModel As cSapModel, ByRef ISapPlugin As cPluginCallback) Implements cPluginContract.Main
        Try
            If ISapPlugin Is Nothing Then
                ' No callback to report through; surface the problem locally and bail.
                MessageBox.Show(
                    "Plugin error: ETABS did not supply a callback handle.",
                    "Story/Frame Logger",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error)
                Return
            End If

            If SapModel Is Nothing Then
                ShowAndLogResult("No model is currently open in ETABS. Open a model and run the plugin again.")
                ISapPlugin.Finish(1)
                Return
            End If

            Dim report As New StringBuilder()
            report.AppendLine("=== ETABS Story/Frame Logger (POC) ===")
            report.AppendLine($"Run time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            report.AppendLine()

            AppendStorySummary(SapModel, report)
            AppendFrameSummary(SapModel, report)

            ShowAndLogResult(report.ToString())
            ISapPlugin.Finish(0)

        Catch ex As Exception
            ' Any unexpected COM/API failure lands here instead of crashing ETABS.
            MessageBox.Show(
                "Story/Frame Logger failed: " & ex.Message,
                "Story/Frame Logger",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error)
            If ISapPlugin IsNot Nothing Then ISapPlugin.Finish(1)
        End Try
    End Sub

    ''' <summary>
    ''' Text ETABS displays for this plugin in the External Plugin list.
    ''' </summary>
    Public Function Info(ByRef Text As String) As Integer Implements cPluginContract.Info
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
