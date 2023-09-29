using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace EnvironmentHelperHost;

public sealed class DebugSendBuffer : Window
{
    public static readonly DebugSendBuffer Instance = new();
    private readonly TextBox _textBox = new();
    private readonly ScrollViewer _scrollViewer = new();
    private DebugSendBuffer()
    {
        
        _scrollViewer.Content = _textBox;
        _scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        _textBox.TextWrapping = TextWrapping.Wrap;
        AddChild(_scrollViewer);
    }

    public void AddMsg(string msg)
    {
        _textBox.AppendText(msg);
        _textBox.AppendText("\n");
        _textBox.ScrollToEnd();
        _scrollViewer.ScrollToBottom();
    }
    
    public void AddData(IEnumerable<byte> msg, int length)
    {
        AddMsg(ArrayToStringB(msg,length));
    }
    
    public void AddRecvData(IEnumerable<byte> msg, int length)
    {
        AddMsg("Recv: -> " + ArrayToStringB(msg,length));
    }
    
    string ArrayToStringB(IEnumerable<byte> array, int length){
        var sb = new StringBuilder("");
        int i = 0;
        foreach (var t in array)
        {
            sb.Append("").Append(Convert.ToString(t.ToString("X2"))).Append(' ');
            i++;
            if (i >= length)
            {
                break;
            }
        }
        
        // sb.Append('');
        return sb.ToString();
    }
}