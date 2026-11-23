using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using TokenLimitWatch.Poc;
using ShapePath = System.Windows.Shapes.Path;
using static TokenLimitWatch.Loc;

namespace TokenLimitWatch;

// Screen layouts follow the Design canvas boards 01–05 (400×720 window, 320px mini widget).
public sealed partial class MonitorWindow
{
    private TextBlock? _refreshLabel;
    private static Brush Ink(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    private static readonly Brush Line = Ink("#E4E4E7");
    private static readonly Brush Divider = Ink("#F4F4F5");
    private static readonly Brush Purple = Ink("#5B5BD6");
    private Border? _footerChrome, _shellRoot, _miniRoot, _logo;
    private readonly StackPanel _miniBody = new();
    private Button? _back, _pin, _minimize, _close;
    private TextBlock? _version;
    private ScrollViewer? _scroll;
    private readonly bool _preview = Environment.GetCommandLineArgs().Contains("--preview");

    private void BuildChrome()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        // Implicit Window styles do not automatically apply to this derived Window class.
        FontFamily = Typography.Ui; FontSize = 14; Foreground = Ink("#18181B"); Background = Ink("#FAFAFA");
        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Ideal);
        // Tabular figures keep percentages and countdowns from shifting as digits change.
        System.Windows.Documents.Typography.SetNumeralAlignment(this,FontNumeralAlignment.Tabular);
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this,new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(4), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(8) });
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/TokenLimitWatch.ico"));
        using(var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TokenLimitWatch.Icon"))
        using(var embedded = new System.Drawing.Icon(stream!)) _tray.Icon = (System.Drawing.Icon)embedded.Clone();

        var shell = new DockPanel();
        var header = new DockPanel { Height = 44, Background = Brushes.White, LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,7,8,7) };
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        _version = Label($"v{version?.Major}.{version?.Minor}.{version?.Build} · Portable",12,"#71717A");
        _version.VerticalAlignment = VerticalAlignment.Center; _version.Margin = new Thickness(0,0,8,0);
        actions.Children.Add(_version);
        _pin = IconButton("고정","항상 위에 고정",() => { Topmost = !Topmost; _settings.AlwaysOnTop = Topmost; Save(); },"pin",16);
        _minimize = IconButton("최소화","최소화",() => WindowState = WindowState.Minimized,"minus",16);
        actions.Children.Add(_pin); actions.Children.Add(_minimize);
        _close = IconButton("닫기","트레이로 숨기기",Close,"close",16); actions.Children.Add(_close);
        DockPanel.SetDock(actions,Dock.Right); header.Children.Add(actions);
        _back = IconButton("뒤로","뒤로",() => Render(_page == "accounts" ? "services" : "dashboard"),"back",18);
        _back.Margin = new Thickness(8,0,0,0); DockPanel.SetDock(_back,Dock.Left); header.Children.Add(_back);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        _logo = Logo(22,14); _logo.Margin = new Thickness(16,0,8,0); brand.Children.Add(_logo);
        _heading.FontFamily = Typography.Ui; brand.Children.Add(_heading); header.Children.Add(brand);
        header.MouseLeftButtonDown += (_,e) => Drag(e);
        var top = new Border { Child = header, BorderThickness = new Thickness(0,0,0,1), BorderBrush = Divider };
        DockPanel.SetDock(top,Dock.Top); shell.Children.Add(top);

        _footer.Orientation = Orientation.Vertical; _footer.HorizontalAlignment = HorizontalAlignment.Stretch;
        _footerChrome = new Border { Child = _footer, Background = Brushes.White, BorderBrush = Divider, BorderThickness = new Thickness(0,1,0,0) };
        DockPanel.SetDock(_footerChrome,Dock.Bottom); shell.Children.Add(_footerChrome);
        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
        shell.Children.Add(_scroll);
        _shellRoot = new Border { Child = shell, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = Ink("#FAFAFA"), ClipToBounds = true };

        // Board 05: borderless white widget, sized to its content so it never scrolls.
        _miniRoot = new Border { Child = _miniBody, Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14,10,12,12) };
        _miniRoot.MouseLeftButtonDown += (_,e) => Drag(e);
        Content = _shellRoot;
        WatchManualResize();
    }

    // The dashboard opens tall enough to show every card without scrolling (capped by the screen).
    // Once the user resizes the window by hand, their size wins and auto-fit stops.
    private bool _autoFit = true, _centered;
    private double _fitHeight = 720;
    private void WatchManualResize() => SizeChanged += (_,e) =>
    {
        if(IsLoaded && !_mini && (Math.Abs(e.NewSize.Height-_fitHeight)>1 || Math.Abs(e.NewSize.Width-400)>1)) _autoFit = false;
    };
    private void FitDashboard()
    {
        if(_mini || !_autoFit || _page != "dashboard" || !IsLoaded) return;
        var width = Width - 2;
        _body.Measure(new Size(width,double.PositiveInfinity));
        _footerChrome!.Measure(new Size(width,double.PositiveInfinity));
        var area = SystemParameters.WorkArea;
        var need = 45 + _footerChrome.DesiredSize.Height + _body.DesiredSize.Height + 2;
        var height = Math.Ceiling(Math.Clamp(need,MinHeight,area.Height - 16));
        _fitHeight = height;
        if(Math.Abs(height - ActualHeight) >= 1) Height = height;
        if(!_centered) { _centered = true; Top = area.Top + (area.Height - height) / 2; }
        else if(Top + height > area.Bottom - 8) Top = Math.Max(area.Top + 8,area.Bottom - height - 8);
    }

    private void Drag(System.Windows.Input.MouseButtonEventArgs e)
    {
        if(e.OriginalSource is ButtonBase) return;
        try { DragMove(); } catch(InvalidOperationException) {}
    }

    private void SetMini(bool mini)
    {
        _mini = mini;
        if(mini)
        {
            Content = _miniRoot; ResizeMode = ResizeMode.NoResize; MinWidth = 0; MinHeight = 0;
            Width = 320; SizeToContent = SizeToContent.Height; Background = Brushes.White; Topmost = true;
        }
        else
        {
            Content = _shellRoot; SizeToContent = SizeToContent.Manual; ResizeMode = ResizeMode.CanResize;
            MinWidth = 320; MinHeight = 336; Width = 400; Height = _fitHeight; Background = Ink("#FAFAFA"); Topmost = _settings.AlwaysOnTop;
        }
    }

    private void ApplyChrome(bool pageChanged)
    {
        var sub = _page is "accounts" or "settings";
        var settings = _page == "settings";
        _back!.Visibility = sub ? Visibility.Visible : Visibility.Collapsed;
        _logo!.Visibility = sub ? Visibility.Collapsed : Visibility.Visible;
        _pin!.Visibility = _page == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        _minimize!.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        _version!.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        foreach(var (b,tip) in new[]{(_pin!,"항상 위에 고정"),(_minimize!,"최소화"),(_close!,"트레이로 숨기기"),(_back!,"뒤로")}) { b.ToolTip=T(tip); AutomationProperties.SetName(b,T(tip)); }
        _heading.Text = settings ? T("설정") : AppName;
        _heading.FontSize = settings ? 15 : 14;
        _heading.FontWeight = settings ? FontWeights.Bold : FontWeights.SemiBold;
        _heading.Margin = new Thickness(sub ? 6 : 0,0,0,0);
        _body.Margin = _page switch { "services" or "accounts" => new Thickness(20,24,20,20), "settings" => new Thickness(16,12,16,12), _ => new Thickness(16) };
        _footerChrome!.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        _footerChrome.Padding = _page == "dashboard" ? new Thickness(16,10,12,10) : new Thickness(20,16,20,20);
        if(pageChanged) _scroll?.ScrollToTop();
    }

    // ---------- primitives ----------
    // WPF breaks Hangul between any two syllables; word joiners emulate CSS word-break: keep-all.
    private static string KeepAll(string text) => System.Text.RegularExpressions.Regex.Replace(text,@"(?<=[가-힣])(?=[가-힣.,])","⁠");
    private TextBlock Label(string text,double size=14,string color="#18181B",bool bold=false,FontWeight? weight=null) =>
        new() { Text=KeepAll(text),FontSize=size, Foreground=Ink(color), TextWrapping=TextWrapping.Wrap, FontWeight=weight ?? (bold?FontWeights.SemiBold:FontWeights.Normal), FontFamily=Typography.Ui, LineHeight=Math.Round(size*1.4) };
    private static Border Box(UIElement child, Thickness? padding=null) => new() { Child=child, Background=Brushes.White, BorderBrush=Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(12), Padding=padding??new Thickness(16), Margin=new Thickness(0,0,0,12) };
    private static Border Rule(double spacing) => new() { Height=1, Background=Divider, Margin=new Thickness(0,spacing,0,spacing) };
    private Button ActionButton(string caption,Action action,bool primary=false)
    {
        var b=Button(caption,action); if(primary) b.Style=(Style)FindResource("Primary");
        AutomationProperties.SetName(b,caption); return b;
    }
    private Button Link(string caption,Action action)
    {
        var b=ActionButton(caption,action); b.Style=(Style)FindResource("Ghost"); b.Foreground=Ink("#4E4EC2"); b.FontSize=13; b.Padding=new Thickness(4,2,4,2); b.MinHeight=0;
        return b;
    }
    private FrameworkElement Glyph(string name,double size=17,string stroke="#3F3F46")
    {
        var data=name switch {
            "refresh" => "M20 11A8 8 0 1 0 17.7 16.7 M20 5V11H14",
            "user" => "M12 3A9 9 0 1 0 12 21A9 9 0 1 0 12 3 M12 7A3 3 0 1 0 12 13A3 3 0 1 0 12 7 M6.5 18C7.7 15.6 9.5 14.5 12 14.5C14.5 14.5 16.3 15.6 17.5 18",
            "expand" => "M14 10L20 4 M15 4H20V9 M10 14L4 20 M9 20H4V15",
            "settings" => "M4 7H14 M18 7H20 M4 17H6 M10 17H20 M16 5A2 2 0 1 0 16 9A2 2 0 1 0 16 5 M8 15A2 2 0 1 0 8 19A2 2 0 1 0 8 15",
            "pin" => "M9 4H15L14 10L17 13V14.5H7V13L10 10Z M12 14.5V20",
            "minus" => "M5 12H19", "close" => "M6 6L18 18 M18 6L6 18",
            "back" => "M15 5L8 12L15 19", "check" => "M5 12.5L9.5 17L19 7.5",
            "info" => "M12 3A9 9 0 1 0 12 21A9 9 0 1 0 12 3 M12 8V13 M12 16V16.01",
            "shield" => "M12 3L19 6V11C19 15.5 16 19 12 21C8 19 5 15.5 5 11V6Z M9 12L11 14L15 10",
            _ => "M6 9L12 15L18 9" };
        return new Viewbox { Width=size,Height=size,Child=new ShapePath { Data=Geometry.Parse(data), Stroke=Ink(stroke),StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=24,Height=24,Stretch=Stretch.None } };
    }
    private Button IconButton(string caption,string tooltip,Action action,string icon,double size=18)
    {
        var b=ActionButton(caption,action); b.Style=(Style)FindResource("Ghost"); b.Content=Glyph(icon,size); b.ToolTip=tooltip;b.Width=30;b.Height=30;b.MinHeight=30;b.Padding=new Thickness(0);
        AutomationProperties.SetName(b,tooltip); return b;
    }
    private static Border Logo(double size,double glyph)
    {
        var wave = new ShapePath {Data=Geometry.Parse("M3 12H7L10 4L14 20L17 12H21"),Stroke=Brushes.White,StrokeThickness=1.8,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=24,Height=24};
        return new Border {Width=size,Height=size,CornerRadius=new CornerRadius(size>20?6:4),Background=Purple,Child=new Viewbox{Width=glyph,Height=glyph,Child=wave},VerticalAlignment=VerticalAlignment.Center};
    }
    private Border Tile(string letter,double size,string background,double font,string color="#18181B") => new()
    {
        Width=size,Height=size,Background=Ink(background),CornerRadius=new CornerRadius(8),VerticalAlignment=VerticalAlignment.Center,
        Child=new TextBlock { Text=letter,FontFamily=Typography.Ui,FontSize=font,FontWeight=FontWeights.Bold,Foreground=Ink(color),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center },
    };
    private static (string Fg,string Bg) Tone(string state) => state switch
    {
        "여유" => ("#4E4EC2","#EEEFFE"), "주의" => ("#8A5200","#FEF3E3"), "임박" => ("#B42323","#FFF0F0"), "연결됨" => ("#0B6B42","#E6F6EE"), _ => ("#52525B","#F4F4F5"),
    };
    private static string State(double? used) => used is null ? "확인 불가" : used >= 90 ? "임박" : used >= 70 ? "주의" : "여유";
    private static string BarColor(double? used) => used is null ? "#D4D4D8" : used >= 90 ? "#EB4444" : used >= 70 ? "#F69800" : "#5B5BD6";
    private UIElement Badge(string text)
    {
        var (fg,bg)=Tone(text);
        var label=Label(T(text),12,fg,true); label.VerticalAlignment=VerticalAlignment.Center; label.TextWrapping=TextWrapping.NoWrap;
        return new Border { Child=label,Background=Ink(bg),CornerRadius=new CornerRadius(4),Padding=new Thickness(8,0,8,0),Height=24,VerticalAlignment=VerticalAlignment.Center };
    }
    private static bool Connected(Slot slot) => !slot.LoginRequired && (slot.LastUpdate.HasValue || slot.Message == "연결됨");
    private static string Letter(Slot slot) => slot.Config.Id == "claude" ? "C" : "G";
    private void Hero(string step,string title,string subtitle)
    {
        var p=new StackPanel {Margin=new Thickness(0,0,0,20)};
        p.Children.Add(Label(step,13,"#4E4EC2",true));
        var h=Label(title,20,weight:FontWeights.Bold);h.LineHeight=28;h.Margin=new Thickness(0,8,0,0);p.Children.Add(h);
        var s=Label(subtitle,14,"#71717A");s.Margin=new Thickness(0,8,0,0);p.Children.Add(s);
        _body.Children.Add(p);
    }
    private void FooterPrimary(string note,string caption,Action action,bool enabled=true)
    {
        _footer.Children.Add(Label(note,13,"#71717A"));
        var button=ActionButton(caption,action,true);button.IsEnabled=enabled;button.Height=44;button.Margin=new Thickness(0,10,0,0);
        _footer.Children.Add(button);
    }

    // ---------- 01 서비스 선택 ----------
    private void DesignServices()
    {
        Hero("STEP 1 / 2",T("모니터링할 AI 서비스를 선택하세요"),T("여러 개를 고를 수 있어요. 선택한 순서대로 대시보드에 표시됩니다."));
        var list=new StackPanel{Margin=new Thickness(0,0,0,20)};
        foreach(var slot in _slots)
        {
            var selected=_settings.Services.Contains(slot.Config.Id);
            var row=new DockPanel{LastChildFill=true};
            var check=new Border{Width=22,Height=22,CornerRadius=new CornerRadius(6),Background=selected?Purple:Brushes.White,BorderBrush=selected?Purple:Ink("#D4D4D8"),BorderThickness=new Thickness(selected?0:1.5),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};
            if(selected) check.Child=Glyph("check",16,"#FFFFFF");
            DockPanel.SetDock(check,Dock.Right);row.Children.Add(check);
            var tile=Tile(Letter(slot),36,selected?"#FFFFFF":"#F4F4F5",16);tile.Margin=new Thickness(0,0,12,0);DockPanel.SetDock(tile,Dock.Left);row.Children.Add(tile);
            var texts=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            texts.Children.Add(Label(slot.Config.Name,15,bold:true));
            var desc=Label(T(slot.Config.Id=="claude"?"5시간 세션 · 주간 한도":"공유 플랜 5시간 · 주간 한도"),13,"#52525B");desc.Margin=new Thickness(0,2,0,0);texts.Children.Add(desc);
            row.Children.Add(texts);
            var select=ActionButton(F("{0} 선택",slot.Config.Name),() => { if(_settings.Services.Contains(slot.Config.Id)) _settings.Services.Remove(slot.Config.Id); else { _settings.Services.Add(slot.Config.Id); slot.Due=DateTimeOffset.MinValue; } Save(); Render(); });
            select.Style=(Style)FindResource("Card");select.Content=row;select.Padding=new Thickness(14,12,14,12);
            select.Background=selected?Ink("#EEEFFE"):Brushes.White;select.BorderBrush=selected?Purple:Line;select.BorderThickness=new Thickness(selected?1.5:1);select.Margin=new Thickness(0,0,0,8);
            AutomationProperties.SetItemStatus(select,T(selected?"선택됨":"선택 안 됨"));
            list.Children.Add(select);
        }
        _body.Children.Add(list);
        var caption=Label(T("추후 지원 예정"),13,"#71717A",true);caption.Margin=new Thickness(4,0,4,8);_body.Children.Add(caption);
        foreach(var (letter,name,desc) in new[]{("GC","GitHub Copilot","월간 프리미엄 요청"),("Cu","Cursor","월간 요청 · 사용 금액")})
        {
            var row=new DockPanel();var badge=Badge("추후 지원");DockPanel.SetDock(badge,Dock.Right);row.Children.Add(badge);
            var tile=Tile(letter,36,"#FFFFFF",16,"#52525B");tile.Margin=new Thickness(0,0,12,0);DockPanel.SetDock(tile,Dock.Left);row.Children.Add(tile);
            var texts=new StackPanel{VerticalAlignment=VerticalAlignment.Center};texts.Children.Add(Label(name,15,"#52525B",true));var d=Label(T(desc),13,"#71717A");d.Margin=new Thickness(0,2,0,0);texts.Children.Add(d);row.Children.Add(texts);
            _body.Children.Add(new Border{Child=row,Background=Ink("#F4F4F5"),BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(14,12,14,12),Margin=new Thickness(0,0,0,8)});
        }
        FooterPrimary(F("{0}개 선택됨",_settings.Services.Count),T("다음"),() => Render("accounts"),_settings.Services.Count>0);
    }

    // ---------- 02 로그인 ----------
    private void DesignAccounts()
    {
        Hero("STEP 2 / 2",T("서비스에 로그인하세요"),T("이메일 주소로 로그인합니다. 로그인 정보는 이 PC에만 암호화해 저장합니다."));
        foreach(var slot in Selected)
        {
            var connected=Connected(slot);
            var content=new StackPanel();
            var title=new DockPanel();
            var status=Badge(slot.Busy&&!connected?"확인 중":connected?"연결됨":"미연결");DockPanel.SetDock(status,Dock.Right);title.Children.Add(status);
            var tile=Tile(Letter(slot),32,"#F4F4F5",15);tile.Margin=new Thickness(0,0,10,0);DockPanel.SetDock(tile,Dock.Left);title.Children.Add(tile);
            var name=Label(slot.Config.Name,15,bold:true);name.VerticalAlignment=VerticalAlignment.Center;title.Children.Add(name);
            content.Children.Add(title);
            if(connected)
            {
                var account=new DockPanel();
                var logout=Link(T("로그아웃"),() => Logout(slot));logout.IsEnabled=!slot.Busy;DockPanel.SetDock(logout,Dock.Right);account.Children.Add(logout);
                var icon=Glyph("user",18,"#71717A");DockPanel.SetDock(icon,Dock.Left);account.Children.Add(icon);
                var who=Label(T("로그인됨"),13,"#52525B",weight:FontWeights.Medium);who.Margin=new Thickness(8,0,0,0);who.VerticalAlignment=VerticalAlignment.Center;account.Children.Add(who);
                content.Children.Add(new Border{Child=account,Background=Ink("#FAFAFA"),CornerRadius=new CornerRadius(8),Padding=new Thickness(12,8,8,8),Margin=new Thickness(0,14,0,0)});
                if(slot.Config.Id=="claude"&&slot.Organizations.Count>1) {var combo=new ComboBox {ItemsSource=slot.Organizations,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=_settings.ClaudeOrg,Margin=new Thickness(0,12,0,0)};combo.SelectionChanged+=(_,_)=>{_settings.ClaudeOrg=combo.SelectedValue as string;Save();slot.Due=DateTimeOffset.MinValue;};content.Children.Add(combo);}
            }
            else
            {
                var info=Label(T("버튼을 누르면 앱 안에 로그인 창이 열립니다. 평소처럼 이메일로 로그인하면 창이 자동으로 닫힙니다."),13,"#52525B");info.Margin=new Thickness(0,14,0,0);content.Children.Add(info);
                var warn=new DockPanel();var warnIcon=Glyph("info",16,"#8A5200");warnIcon.VerticalAlignment=VerticalAlignment.Top;((FrameworkElement)warnIcon).Margin=new Thickness(0,0,8,0);DockPanel.SetDock(warnIcon,Dock.Left);warn.Children.Add(warnIcon);
                warn.Children.Add(Label(T("Google 계정 로그인은 지원하지 않아요. 이메일 주소로 로그인해 주세요."),12,"#6B4000"));
                content.Children.Add(new Border{Child=warn,Background=Ink("#FEF3E3"),CornerRadius=new CornerRadius(8),Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,14,0,0)});
                var login=ActionButton(slot.Busy?T("연결 확인 중…"):F("{0} 로그인 창 열기",slot.Config.Name),() => Login(slot));login.IsEnabled=!slot.Busy;login.Height=40;login.FontWeight=FontWeights.SemiBold;login.BorderBrush=Ink("#D4D4D8");login.Margin=new Thickness(0,14,0,0);
                content.Children.Add(login);
            }
            var box=Box(content);box.BorderBrush=connected?Line:Purple;box.BorderThickness=new Thickness(connected?1:1.5);_body.Children.Add(box);
        }
        var note=new DockPanel{Margin=new Thickness(4,8,4,0)};var shield=Glyph("shield",16,"#4E4EC2");((FrameworkElement)shield).Margin=new Thickness(0,0,8,0);DockPanel.SetDock(shield,Dock.Left);note.Children.Add(shield);
        var noteText=Label(T("비밀번호는 저장하지 않습니다."),12,"#71717A");noteText.VerticalAlignment=VerticalAlignment.Center;note.Children.Add(noteText);_body.Children.Add(note);
        var total=Selected.Count();var done=Selected.Count(Connected);
        FooterPrimary(done==total?F("{0} / {1} 연결됨",done,total):F("{0} / {1} 연결됨 · 미연결 서비스는 나중에 설정에서 연결할 수 있어요.",done,total),T("모니터링 시작"),()=>Render("dashboard"));
    }

    // ---------- 03 대시보드 ----------
    private void DesignDashboard()
    {
        foreach(var slot in Selected)
        {
            var content=new StackPanel();
            var head=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};
            var tile=Tile(Letter(slot),28,"#F4F4F5",14);tile.Margin=new Thickness(0,0,10,0);head.Children.Add(tile);
            var title=Label(slot.Config.Name,15,bold:true);title.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(title);content.Children.Add(head);
            if(slot.Config.Id=="chatgpt") {var note=Label(T("공유 플랜 한도 · Chat 대화 제외"),12,"#71717A");note.Margin=new Thickness(0,-6,0,12);content.Children.Add(note);}
            var limits=DisplayPolicy.VisibleLimits(slot.Limits,false);
            for(int index=0;index<limits.Count;index++)
            {
                if(index>0) content.Children.Add(Rule(12));
                var limit=limits[index];
                var row=new DockPanel{Margin=new Thickness(0,0,0,8)};
                var badge=Badge(State(limit.Used));DockPanel.SetDock(badge,Dock.Right);row.Children.Add(badge);
                var name=Label(T(limit.Name),14,bold:true);name.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(name);content.Children.Add(row);
                var bar=new ProgressBar{Minimum=0,Maximum=100,Value=Math.Clamp(limit.Used??0,0,100),Foreground=Ink(BarColor(limit.Used)),Height=6,Margin=new Thickness(0,0,0,10)};
                AutomationProperties.SetName(bar,F("{0} 사용량 {1}",T(limit.Name),limit.UsedText));content.Children.Add(bar);
                var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());grid.RowDefinitions.Add(new());grid.RowDefinitions.Add(new());
                Metric(grid,T("사용량"),limit.UsedText,0,0,true,limit.Used is null);Metric(grid,T("남은량"),limit.RemainingText,0,1,true,limit.Remaining is null);
                Metric(grid,T("초기화 시간"),limit.ResetText,1,0,false,limit.Reset is null);
                var countdown=Metric(grid,T("초기화까지 남은 시간"),limit.Countdown(DateTimeOffset.Now),1,1,false,limit.Reset is null);_countdowns.Add((countdown,limit));
                content.Children.Add(grid);
            }
            if(slot.LoginRequired||slot.Message.Contains("실패"))
            {
                var message=Label(T(slot.Message),12,"#71717A");message.Margin=new Thickness(0,10,0,0);content.Children.Add(message);
                if(slot.LoginRequired){var login=Link(T("로그인"),()=>Render("accounts"));login.HorizontalAlignment=HorizontalAlignment.Left;login.Margin=new Thickness(-4,2,0,0);content.Children.Add(login);}
            }
            _body.Children.Add(Box(content,new Thickness(16,14,16,14)));
        }
        if(!Selected.Any()) {Hero("TOKEN LIMIT WATCH",T("사용량을 한눈에"),T("AI 서비스를 연결하면 사용량과 초기화 시간을 확인할 수 있어요."));_body.Children.Add(ActionButton(T("서비스 선택"),()=>Render("services"),true));}

        var footer=new DockPanel();
        var refresh=ActionButton(T("지금 갱신"),RefreshAll);var refreshBody=new StackPanel{Orientation=Orientation.Horizontal};refreshBody.Children.Add(Glyph("refresh",14,"#18181B"));
        var refreshText=Label(T("지금 갱신"),13,bold:true);refreshText.Margin=new Thickness(6,0,0,0);refreshText.VerticalAlignment=VerticalAlignment.Center;refreshBody.Children.Add(refreshText);
        refresh.Content=refreshBody;refresh.Height=30;refresh.MinHeight=30;refresh.Padding=new Thickness(10,0,12,0);refresh.BorderBrush=Ink("#D4D4D8");DockPanel.SetDock(refresh,Dock.Left);footer.Children.Add(refresh);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};
        actions.Children.Add(IconButton(T("계정"),T("계정 · 로그인"),()=>Render("accounts"),"user"));
        actions.Children.Add(IconButton(T("미니"),T("미니 위젯으로 전환"),ToggleMini,"expand"));
        actions.Children.Add(IconButton(T("설정"),T("설정"),()=>Render("settings"),"settings"));
        DockPanel.SetDock(actions,Dock.Right);footer.Children.Add(actions);
        _refreshLabel=Label("",12,"#71717A");_refreshLabel.TextWrapping=TextWrapping.NoWrap;_refreshLabel.TextTrimming=TextTrimming.CharacterEllipsis;_refreshLabel.Margin=new Thickness(8,0,8,0);_refreshLabel.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(_refreshLabel);
        UpdateRefreshLabel();
        _footer.Children.Add(footer);
        FitDashboard();
    }
    private TextBlock Metric(Grid grid,string label,string value,int row,int col,bool percentage,bool unknown)
    {
        var panel=new StackPanel{Margin=new Thickness(col==0?0:6,0,col==0?6:0,row==0?10:0)};
        panel.Children.Add(Label(label,12,"#71717A"));
        var text=Label("",unknown?14:percentage?16:15,unknown?"#71717A":"#18181B",weight:unknown?FontWeights.Medium:FontWeights.Bold);text.LineHeight=22;text.TextWrapping=TextWrapping.NoWrap;
        if(percentage&&!unknown&&value.EndsWith('%'))
        {
            text.Inlines.Add(new Run(value[..^1]));
            text.Inlines.Add(new Run("%"){FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Ink("#71717A")});
        }
        else text.Text=value;
        panel.Children.Add(text);Grid.SetColumn(panel,col);Grid.SetRow(panel,row);grid.Children.Add(panel);return text;
    }
    private void UpdateRefreshLabel()
    {
        if(_refreshLabel is null) return;
        var last=Selected.Where(s=>s.LastUpdate.HasValue).Select(s=>s.LastUpdate).Max()?.ToString("HH:mm")??"—";
        var busy=Selected.Any(s=>s.Busy);
        _refreshLabel.Text=_mini?$"{AppName} · {(busy?T("갱신 중…"):last)}":busy?T("갱신 중…"):F("{0} 갱신 · {1}분마다",last,_settings.Interval);
    }

    // ---------- 05 미니 위젯 ----------
    private void DesignMini()
    {
        var head=new DockPanel();
        var open=IconButton(T("대시보드"),T("대시보드 열기"),ToggleMini,"expand",14);DockPanel.SetDock(open,Dock.Right);head.Children.Add(open);
        var logo=Logo(16,11);logo.Margin=new Thickness(0,0,6,0);DockPanel.SetDock(logo,Dock.Left);head.Children.Add(logo);
        _refreshLabel=MiniLabel("",13,"#52525B",true);_refreshLabel.VerticalAlignment=VerticalAlignment.Center;_refreshLabel.TextWrapping=TextWrapping.NoWrap;head.Children.Add(_refreshLabel);
        _miniBody.Children.Add(head);UpdateRefreshLabel();
        var rows=Selected.SelectMany(s=>DisplayPolicy.VisibleLimits(s.Limits,true).Select(l=>(Slot:s,Limit:l))).ToList();
        for(var i=0;i<rows.Count;i++)
        {
            var (slot,limit)=rows[i];
            _miniBody.Children.Add(i==0?new Border{Height=8}:Rule(8));
            var title=new DockPanel();
            var name=MiniLabel(F("{0} · 5시간",slot.Config.Name),15,bold:true);name.TextWrapping=TextWrapping.NoWrap;name.Margin=new Thickness(0,0,8,0);name.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(name,Dock.Left);title.Children.Add(name);
            var bar=new ProgressBar{Minimum=0,Maximum=100,Value=Math.Clamp(limit.Used??0,0,100),Foreground=Ink(BarColor(limit.Used)),Height=6,VerticalAlignment=VerticalAlignment.Center};
            AutomationProperties.SetName(bar,F("{0} 5시간 사용량 {1}",slot.Config.Name,limit.UsedText));title.Children.Add(bar);_miniBody.Children.Add(title);
            var grid=new Grid{Margin=new Thickness(0,5,0,0)};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());
            var used=MiniMetric(T("사용량"),limit.UsedText,limit.Used is null);grid.Children.Add(used.Panel);
            var left=MiniMetric(T("남은량"),limit.RemainingText,limit.Remaining is null);Grid.SetColumn(left.Panel,1);grid.Children.Add(left.Panel);
            _miniBody.Children.Add(grid);
            var reset=MiniMetric(T("초기화"),limit.ResetText,limit.Reset is null);reset.Panel.Margin=new Thickness(0,5,0,0);_miniBody.Children.Add(reset.Panel);
            var countdown=MiniMetric(T("남은 시간"),limit.Countdown(DateTimeOffset.Now),limit.Reset is null);countdown.Panel.Margin=new Thickness(0,5,0,0);_miniBody.Children.Add(countdown.Panel);
            _countdowns.Add((countdown.Value,limit));
            if(slot.LoginRequired||slot.Message.Contains("실패")) {var message=MiniLabel(T(slot.Message),12.5,"#71717A");message.Margin=new Thickness(0,5,0,0);_miniBody.Children.Add(message);}
        }
        if(rows.Count==0) {var empty=MiniLabel(T("연결된 서비스가 없어요. 대시보드에서 서비스를 선택하세요."),13,"#71717A");empty.Margin=new Thickness(0,8,0,0);_miniBody.Children.Add(empty);}
    }
    private (StackPanel Panel,TextBlock Value) MiniMetric(string label,string value,bool unknown)
    {
        var panel=new StackPanel{Orientation=Orientation.Horizontal};
        var caption=MiniLabel(label,13,"#71717A");caption.TextWrapping=TextWrapping.NoWrap;caption.Margin=new Thickness(0,0,6,0);caption.VerticalAlignment=VerticalAlignment.Bottom;panel.Children.Add(caption);
        var text=MiniLabel(value,14,unknown?"#71717A":"#18181B",weight:unknown?FontWeights.Medium:FontWeights.Bold);text.TextWrapping=TextWrapping.NoWrap;text.VerticalAlignment=VerticalAlignment.Bottom;panel.Children.Add(text);
        return (panel,text);
    }

    // Mini widget text uses embedded Pretendard for Hangul, Latin and numbers alike.
    private TextBlock MiniLabel(string text,double size,string color="#18181B",bool bold=false,FontWeight? weight=null)
    {
        var label=Label(text,size,color,bold,weight);label.FontFamily=(FontFamily)FindResource("BodyFont");return label;
    }
    // ---------- 04 설정 ----------
    private void DesignSettings()
    {
        var segment=new UniformGrid{Columns=4};
        foreach(var minutes in new[]{3,5,10,30})
        {
            var on=minutes==_settings.Interval;
            var b=ActionButton(F("{0}분",minutes),()=>{_settings.Interval=minutes;Save();foreach(var slot in Selected)slot.Due=DateTimeOffset.Now.AddMinutes(minutes);Render();});
            b.Style=(Style)FindResource("Ghost");b.Padding=new Thickness(0);b.MinHeight=0;b.FontSize=13;b.Margin=new Thickness(1,0,1,0);
            b.FontWeight=on?FontWeights.SemiBold:FontWeights.Medium;b.Foreground=Ink(on?"#18181B":"#52525B");
            if(on){b.Background=Brushes.White;b.BorderBrush=Line;b.BorderThickness=new Thickness(1);}
            AutomationProperties.SetItemStatus(b,on?T("선택됨"):"");segment.Children.Add(b);
        }
        var intervals=new Border{Child=segment,Background=Divider,CornerRadius=new CornerRadius(8),Padding=new Thickness(3),Width=200,Height=32};
        var retry=new DockPanel();var info=Glyph("info",14,"#71717A");((FrameworkElement)info).VerticalAlignment=VerticalAlignment.Top;((FrameworkElement)info).Margin=new Thickness(0,1,8,0);DockPanel.SetDock(info,Dock.Left);retry.Children.Add(info);
        retry.Children.Add(Label(T("정보를 가져오지 못하면 5분마다 다시 시도하고, 성공하면 원래 간격으로 돌아갑니다."),12,"#52525B"));
        Section(T("갱신"),Row(T("갱신 간격"),intervals,T("기본 10분")),retry);

        var thresholdText=Label(_settings.Warnings?$"{_settings.Threshold}%":T("끄기"),14);
        var chevron=Glyph("chevron",16,"#71717A");DockPanel.SetDock(chevron,Dock.Right);
        var thresholdBody=new DockPanel();thresholdBody.Children.Add(chevron);thresholdBody.Children.Add(thresholdText);
        var threshold=ActionButton(T("사용률 경고"),()=>{});threshold.Content=thresholdBody;threshold.Width=120;threshold.Height=32;threshold.MinHeight=32;threshold.Padding=new Thickness(12,0,10,0);threshold.HorizontalContentAlignment=HorizontalAlignment.Stretch;threshold.BorderBrush=Ink("#D4D4D8");
        var menu=new ContextMenu();
        foreach(var n in new double[]{70,80,90,95}){var item=new MenuItem{Header=n+"%",IsCheckable=true,IsChecked=_settings.Warnings&&_settings.Threshold==n};item.Click+=(_,_)=>{_settings.Threshold=n;_settings.Warnings=true;Save();Render();};menu.Items.Add(item);}
        menu.Items.Add(new Separator());
        var off=new MenuItem{Header=T("끄기"),IsCheckable=true,IsChecked=!_settings.Warnings};off.Click+=(_,_)=>{_settings.Warnings=false;Save();Render();};menu.Items.Add(off);
        threshold.Click+=(_,_)=>{menu.PlacementTarget=threshold;menu.IsOpen=true;};
        Section(T("알림"),Row(T("사용률 경고"),threshold),Toggle(T("초기화되면 알림"),_settings.ResetNotifications,v=>_settings.ResetNotifications=v));

        bool startup;
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startup=key?.GetValue(RunValue)!=null;
        var clean=ActionButton(T("삭제"),()=>{Startup(false);Render();});clean.FontSize=13;clean.FontWeight=FontWeights.SemiBold;clean.Foreground=Ink("#B42323");clean.BorderBrush=Ink("#D4D4D8");clean.Height=32;clean.MinHeight=32;clean.Padding=new Thickness(14,0,14,0);
        AutomationProperties.SetName(clean,T("등록 흔적 삭제"));
        Section(T("창과 실행"),
            Toggle(T("항상 위에 표시"),_settings.AlwaysOnTop,v=>Topmost=_settings.AlwaysOnTop=v),
            Toggle(T("닫기 버튼을 누르면 트레이로"),_settings.CloseToTray,v=>_settings.CloseToTray=v),
            Toggle(T("Windows 시작 시 실행"),startup,Startup,T("현재 사용자(HKCU)에만 등록 · 관리자 권한 불필요")),
            Row(T("등록 흔적 삭제"),clean,T("알림·자동 실행 때문에 등록된 항목을 모두 지워요")));

        var languages=new UniformGrid{Columns=2};
        foreach(var (code,label) in new[]{("ko","한국어"),("en","English")})
        {
            var on=Loc.Lang==code;
            var b=ActionButton(label,()=>{if(Loc.Lang!=code)SetLanguage(code);});
            b.Style=(Style)FindResource("Ghost");b.Padding=new Thickness(0);b.MinHeight=0;b.FontSize=13;b.Margin=new Thickness(1,0,1,0);
            b.FontWeight=on?FontWeights.SemiBold:FontWeights.Medium;b.Foreground=Ink(on?"#18181B":"#52525B");
            if(on){b.Background=Brushes.White;b.BorderBrush=Line;b.BorderThickness=new Thickness(1);}
            AutomationProperties.SetItemStatus(b,on?T("선택됨"):"");languages.Children.Add(b);
        }
        Section(T("언어"),Row(T("표시 언어"),new Border{Child=languages,Background=Divider,CornerRadius=new CornerRadius(8),Padding=new Thickness(3),Width=160,Height=32}));

        var services=new List<UIElement>();
        foreach(var slot in Selected)
        {
            var connected=Connected(slot);
            var row=new DockPanel();
            var link=connected?Link(T("로그아웃"),()=>Logout(slot)):Link(T("로그인"),()=>Login(slot));link.IsEnabled=!slot.Busy;link.Margin=new Thickness(6,0,-4,0);DockPanel.SetDock(link,Dock.Right);row.Children.Add(link);
            var badge=Badge(connected?"연결됨":"미연결");DockPanel.SetDock(badge,Dock.Right);row.Children.Add(badge);
            var name=Label(slot.Config.Name,14,bold:true);name.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(name);
            services.Add(row);
        }
        if(services.Count==0) services.Add(Label(T("선택한 서비스가 없어요."),13,"#71717A"));
        Section(T("연결된 서비스"),services.ToArray());
        var add=ActionButton(T("서비스 추가"),()=>Render("services"));add.Height=32;add.MinHeight=32;add.FontSize=13;add.FontWeight=FontWeights.SemiBold;add.BorderBrush=Ink("#D4D4D8");add.Margin=new Thickness(0,-6,0,12);_body.Children.Add(add);
        var exit=Link(T("앱 종료"),Exit);exit.Foreground=Ink("#71717A");exit.HorizontalAlignment=HorizontalAlignment.Center;_body.Children.Add(exit);
    }
    private UIElement Row(string caption,UIElement control,string? description=null)
    {
        var row=new DockPanel{MinHeight=36};
        if(control is FrameworkElement fe){fe.VerticalAlignment=VerticalAlignment.Center;fe.Margin=new Thickness(12,0,0,0);}
        DockPanel.SetDock(control,Dock.Right);row.Children.Add(control);
        var texts=new StackPanel{VerticalAlignment=VerticalAlignment.Center};texts.Children.Add(Label(caption,14));
        if(description!=null){var d=Label(description,12,"#71717A");d.Margin=new Thickness(0,1,0,0);texts.Children.Add(d);}
        row.Children.Add(texts);return row;
    }
    private UIElement Toggle(string caption,bool value,Action<bool> change,string? description=null)
    {
        var toggle=new CheckBox{IsChecked=value};AutomationProperties.SetName(toggle,caption);
        toggle.Click+=(_,_)=>{try{change(toggle.IsChecked==true);Save();}catch(Exception ex){Report(ex);}};
        return Row(caption,toggle,description);
    }
    private void Section(string caption,params UIElement[] rows)
    {
        var group=new StackPanel{Margin=new Thickness(0,0,0,12)};
        var title=Label(caption,13,"#71717A",true);title.Margin=new Thickness(4,0,4,6);group.Children.Add(title);
        var stack=new StackPanel();
        for(var i=0;i<rows.Length;i++) stack.Children.Add(new Border{Child=rows[i],Padding=new Thickness(16,8,16,8),BorderBrush=Divider,BorderThickness=new Thickness(0,0,0,i<rows.Length-1?1:0)});
        group.Children.Add(new Border{Child=stack,Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12)});
        _body.Children.Add(group);
    }
}
