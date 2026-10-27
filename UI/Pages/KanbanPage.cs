using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

public class KanbanList : ListBox
{
    public string Durum { get; }
    public event Action<long, string>? Dropped;
    Point _start;

    public KanbanList(string durum)
    {
        Durum = durum;
        AllowDrop = true;
        SelectionMode = SelectionMode.Single;
        PreviewMouseLeftButtonDown += (_, e) => _start = e.GetPosition(null);
        PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || SelectedItem is not ListBoxItem it || it.Tag is not long id) return;
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            DragDrop.DoDragDrop(this, new DataObject("gorev_id", id), DragDropEffects.Move);
        };
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent("gorev_id") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData("gorev_id") is long id) Dropped?.Invoke(id, Durum);
            e.Handled = true;
        };
    }
}

/// <summary>3 sütunlu Kanban görev panosu (sürükle-bırak).</summary>
public class KanbanPage : BasePage
{
    static readonly string[] Sutunlar = { "Yapılacak", "Devam Ediyor", "Tamamlandı" };
    static readonly string[] Oncelik = { "Düşük", "Orta", "Yüksek" };
    readonly Dictionary<string, KanbanList> _lists = new();

    static List<Field> Fields() => new()
    {
        new("baslik", "Başlık", required: true), new("aciklama", "Açıklama", "multiline"), new("son_tarih", "Son Tarih", "date"),
        new("oncelik", "Öncelik", "combo", Field.Of(Oncelik), def: "Orta"), new("durum", "Durum", "combo", Field.Of(Sutunlar), def: "Yapılacak"),
    };

    public KanbanPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Görevler", "Kartları sütunlar arasında sürükleyin • çift tıklayınca düzenlenir"));
        Top(Ui.HRow(Ui.Btn("＋ Görev", "Primary", Add), Ui.Btn("Sil", "Danger", Delete)));
        var cols = new Grid();
        int i = 0;
        foreach (var s in Sutunlar)
        {
            if (i > 0) cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            var card = new Card();
            card.Top(Ui.H2(s));
            var lw = new KanbanList(s);
            lw.Dropped += OnDrop;
            lw.MouseDoubleClick += (_, _) => Edit();
            lw.SelectionChanged += (o, _) =>
            {
                if (((KanbanList)o).SelectedItem != null)
                    foreach (var other in _lists.Values.Where(x => x != o)) other.SelectedItem = null;
            };
            card.Fill(lw);
            _lists[s] = lw;
            Grid.SetColumn(card, i * 2);
            cols.Children.Add(card);
            i++;
        }
        Fill(cols);
    }

    public override void Refresh()
    {
        foreach (var lw in _lists.Values) lw.Items.Clear();
        foreach (var g in Db.Query("SELECT * FROM gorevler ORDER BY CASE oncelik WHEN 'Yüksek' THEN 0 WHEN 'Orta' THEN 1 ELSE 2 END, son_tarih"))
        {
            var lw = _lists.TryGetValue(g.S("durum"), out var x) ? x : _lists["Yapılacak"];
            var k = Utils.DaysLeft(g.S("son_tarih"));
            var ikon = g.S("oncelik") == "Yüksek" ? "🔴" : g.S("oncelik") == "Orta" ? "🟠" : "🟢";
            var tarih = g.S("son_tarih").Length > 0 ? $"\n📅 {Utils.FmtDate(g.S("son_tarih"))}" : "";
            string brush = "text";
            if (k != null && k < 0 && g.S("durum") != "Tamamlandı") brush = "danger";
            else if (k != null && k <= 2 && g.S("durum") != "Tamamlandı") brush = "warn";
            var it = new ListBoxItem { Tag = g.L("id"), Content = Ui.Text($"{ikon} {g.S("baslik")}{tarih}", 13, false, brush, true) };
            lw.Items.Add(it);
        }
    }

    void OnDrop(long id, string durum)
    {
        Db.Execute("UPDATE gorevler SET durum=? WHERE id=?", durum, id);
        Refresh();
    }

    long? CurrentId()
    {
        foreach (var lw in _lists.Values)
            if (lw.SelectedItem is ListBoxItem it && it.Tag is long id) return id;
        return null;
    }

    public void YeniGorev() => Add();

    void Add()
    {
        var v = FormDialog.Ask(this, "Yeni Görev", Fields());
        if (v != null) { Db.Insert("gorevler", v); Refresh(); }
    }

    void Edit()
    {
        var gid = CurrentId();
        var g = gid != null ? Db.One("SELECT * FROM gorevler WHERE id=?", gid) : null;
        if (g == null) return;
        var v = FormDialog.Ask(this, "Görevi Düzenle", Fields(), g);
        if (v != null) { Db.Update("gorevler", gid!.Value, v); Refresh(); }
    }

    void Delete()
    {
        var gid = CurrentId();
        if (gid != null && Ui.Confirm(this, "Görev silinsin mi?")) { Db.Delete("gorevler", gid.Value); Refresh(); }
    }
}
