using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
internal static class TeamUpdaterAutoStart
{
    [ModuleInitializer]
    internal static void Initialize() => Application.Idle += Ready;
    private static void Ready(object? sender, EventArgs args)
    {
        if(Application.OpenForms.Count==0) return;
        var owner=Application.OpenForms[0];
        if(owner is null || !owner.Visible) return;
        Application.Idle -= Ready;
        try { TeamUpdaterBootstrap.Attach(owner); }
        catch(Exception error){ MessageBox.Show(owner,"更新器接入失败："+error.Message); }
    }
}
