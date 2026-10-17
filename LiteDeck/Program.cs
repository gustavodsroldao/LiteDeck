namespace LiteDeck;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--probe")
        {
            try { Probe.Run(args[1]); } catch (Exception e) { File.AppendAllText(args[1], e.ToString()); }
            return;
        }

        if (args.Length > 1 && args[0] == "--uefidump")
        {
            try { Uefi.Dump(args[1]); } catch (Exception e) { File.WriteAllText(args[1], "ERRO: " + e); }
            return;
        }

        // Teste do Optimus: alterna, confere, volta ao original e confere (o efeito real só vale após reiniciar)
        if (args.Length > 1 && args[0] == "--optimustest")
        {
            var log = new System.Text.StringBuilder();
            bool? orig = null;
            try
            {
                orig = Uefi.OptimusDisabled; log.AppendLine($"original: desativado={orig}");
                Uefi.OptimusDisabled = !orig.Value; log.AppendLine($"alternado: desativado={Uefi.OptimusDisabled}");
            }
            catch (Exception e) { log.AppendLine("ERRO: " + e.Message); }
            finally
            {
                try { if (orig is bool o) { Uefi.OptimusDisabled = o; log.AppendLine($"restaurado: desativado={Uefi.OptimusDisabled}"); } }
                catch (Exception e) { log.AppendLine("ERRO ao restaurar: " + e.Message); }
                File.WriteAllText(args[1], log.ToString());
            }
            return;
        }

        if (args.Length > 1 && args[0] == "--ecwatch")
        {
            try { EcWatch.Run(args[1]); } catch (Exception e) { File.AppendAllText(args[1], e.ToString()); }
            return;
        }

        if (args.Length > 1 && args[0] == "--selftest")
        {
            try { SelfTest.Run(args[1]); } catch (Exception e) { File.AppendAllText(args[1], e.ToString()); }
            return;
        }

        // Captura de tela da interface (sem hardware): --shot arquivo.png [aba]
        if (args.Length > 1 && args[0] == "--shot")
        {
            ApplicationConfiguration.Initialize();
            using var f = new MainForm(null, false);
            f.ShowTab(args.Length > 2 ? int.Parse(args[2]) : 0);
            f.Show(); Application.DoEvents();
            using var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height);
            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
            var crop = bmp.Clone(new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height), bmp.PixelFormat);
            crop.Save(args[1]);
            var content = f.PageContent(args.Length > 2 ? int.Parse(args[2]) : 0);
            using var full = new Bitmap(content.Width, content.Height);
            content.DrawToBitmap(full, new Rectangle(Point.Empty, content.Size));
            full.Save(Path.ChangeExtension(args[1], ".full.png"));
            return;
        }

        using var mutex = new Mutex(true, "LiteDeck.SingleInstance", out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Hw hw;
        try { hw = new Hw(); }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "LiteDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        bool hidden = args.Contains("--tray");
        var form = new MainForm(hw, hidden);
        if (hidden) Application.Run(new HiddenContext(form)); else Application.Run(form);
        hw.Dispose();
    }

    /// <summary>Mantém o app vivo só na bandeja, sem mostrar a janela principal.</summary>
    sealed class HiddenContext : ApplicationContext
    {
        public HiddenContext(MainForm f) { f.FormClosed += (_, _) => ExitThread(); _ = f.Handle; }
    }
}
