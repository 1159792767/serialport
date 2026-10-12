// Generated integration: call Attach once in your main Form constructor.
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Team.DesktopUpdater;
public static class TeamUpdaterBootstrap
{
    public static void Attach(Form owner)
    {
        if (owner.Controls.ContainsKey("TeamUpdaterEntry")) return;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "updater-settings.json")));
        var config = json.RootElement;
        var assembly = Assembly.GetEntryAssembly() ?? throw new InvalidOperationException("No application assembly");
        var version = assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var client = new UpdateClient(new UpdateOptions(
            config.GetProperty("appId").GetString()!, version, assembly.GetName().Name + ".exe",
            new Uri(config.GetProperty("manifestUrl").GetString()!),
            config.GetProperty("downloadPrefix").GetString()!));
        var button = new Button { Name = "TeamUpdaterEntry", Text = "更新 / 回退", AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right, Left = Math.Max(0, owner.ClientSize.Width - 130), Top = 8 };
        button.Click += (_, _) => UpdateDialog.Show(owner, client, Application.Exit);
        owner.Controls.Add(button); button.BringToFront();
        void Register() {
            try { client.RegisterCurrent(); }
            catch (Exception error) { button.Enabled = false; MessageBox.Show(owner, "更新器初始化失败：" + error.Message); }
        }
        if(owner.Visible) Register(); else owner.Shown += (_, _) => Register();
        bool checking = false;
        async void Check() {
            if(checking || owner.IsDisposed || !button.Enabled) return;
            checking=true;
            try { var latest=await client.CheckAsync(); if(!button.IsDisposed) button.Text=latest is null?"更新 / 回退":"发现更新 "+latest.Version; }
            catch(Exception error){ if(!button.IsDisposed){button.Text="检查更新 / 重试";System.Diagnostics.Trace.WriteLine("Update check: "+error.GetBaseException().Message);} }
            finally{checking=false;}
        }
        var timer=new System.Windows.Forms.Timer{Interval=15*60*1000};timer.Tick+=(_,_)=>Check();timer.Start();owner.FormClosed+=(_,_)=>timer.Dispose();
        if(owner.Visible) Check(); else owner.Shown+=(_,_)=>Check();
    }
}
