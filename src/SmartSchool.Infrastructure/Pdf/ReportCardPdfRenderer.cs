using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartSchool.Application.Common;
using SmartSchool.Shared.Contracts;

namespace SmartSchool.Infrastructure.Pdf;

/// <summary>A4 report card built with QuestPDF. Arabic is laid out right to left.</summary>
public class ReportCardPdfRenderer : IReportCardPdf
{
    static ReportCardPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Use installed fonts (Arial/Tahoma on Windows, Noto/DejaVu on Linux) so Arabic renders.
        // Font files placed next to the application are picked up as well.
        QuestPDF.Settings.UseSystemFonts = true;
    }

    // Arial and Tahoma ship with Windows and include Arabic glyphs; DejaVu/Noto cover Linux servers.
    // QuestPDF stops if any listed family is missing, so list only fonts each OS ships with.
    // On Linux install the "fonts-noto-core" (or DejaVu) package for Arabic. Lato is bundled with QuestPDF.
    private static readonly string[] Fonts = OperatingSystem.IsWindows()
        ? ["Arial", "Tahoma", "Lato"]
        : ["Noto Sans", "Noto Sans Arabic", "Lato"];

    private static readonly Dictionary<string, Dictionary<string, string>> Labels = new()
    {
        ["en"] = new()
        {
            ["title"] = "Report card", ["student"] = "Student", ["no"] = "Student no.", ["dob"] = "Date of birth", ["class"] = "Class",
            ["year"] = "Academic year", ["term"] = "Term", ["homeroom"] = "Homeroom teacher", ["subject"] = "Subject", ["coef"] = "Coef.",
            ["avg"] = "Average /20", ["classAvg"] = "Class avg.", ["teacher"] = "Teacher", ["comment"] = "Teacher's comment",
            ["overall"] = "Overall average", ["rank"] = "Rank", ["of"] = "of", ["attendance"] = "Attendance", ["absences"] = "Days absent",
            ["unjustified"] = "Unjustified", ["late"] = "Late arrivals", ["general"] = "General comment", ["signature"] = "Head of school",
            ["parent"] = "Parent / guardian", ["draft"] = "DRAFT, not published", ["generated"] = "Generated on", ["page"] = "Page"
        },
        ["fr"] = new()
        {
            ["title"] = "Bulletin scolaire", ["student"] = "Élève", ["no"] = "N° élève", ["dob"] = "Date de naissance", ["class"] = "Classe",
            ["year"] = "Année scolaire", ["term"] = "Trimestre", ["homeroom"] = "Professeur principal", ["subject"] = "Matière", ["coef"] = "Coef.",
            ["avg"] = "Moyenne /20", ["classAvg"] = "Moy. classe", ["teacher"] = "Enseignant", ["comment"] = "Appréciation",
            ["overall"] = "Moyenne générale", ["rank"] = "Rang", ["of"] = "sur", ["attendance"] = "Assiduité", ["absences"] = "Jours d'absence",
            ["unjustified"] = "Non justifiées", ["late"] = "Retards", ["general"] = "Appréciation générale", ["signature"] = "Chef d'établissement",
            ["parent"] = "Parent / tuteur", ["draft"] = "BROUILLON, non publié", ["generated"] = "Édité le", ["page"] = "Page"
        },
        ["ar"] = new()
        {
            ["title"] = "كشف النقاط", ["student"] = "التلميذ", ["no"] = "رقم التلميذ", ["dob"] = "تاريخ الازدياد", ["class"] = "القسم",
            ["year"] = "السنة الدراسية", ["term"] = "الفصل", ["homeroom"] = "الأستاذ الرئيسي", ["subject"] = "المادة", ["coef"] = "المعامل",
            ["avg"] = "المعدل /20", ["classAvg"] = "معدل القسم", ["teacher"] = "الأستاذ", ["comment"] = "ملاحظة الأستاذ",
            ["overall"] = "المعدل العام", ["rank"] = "الرتبة", ["of"] = "من", ["attendance"] = "المواظبة", ["absences"] = "أيام الغياب",
            ["unjustified"] = "غير مبررة", ["late"] = "التأخرات", ["general"] = "الملاحظة العامة", ["signature"] = "مدير المؤسسة",
            ["parent"] = "ولي الأمر", ["draft"] = "مسودة، غير منشور", ["generated"] = "تاريخ الإصدار", ["page"] = "صفحة"
        }
    };

    public byte[] Render(ReportCardDto card, string language)
    {
        var t = Labels.GetValueOrDefault(language) ?? Labels["en"];
        var rtl = language == "ar";
        var culture = CultureInfo.GetCultureInfo(language switch { "fr" => "fr-FR", "ar" => "ar-MA", _ => "en-GB" });
        string N(decimal? v) => v?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";
        string D(DateOnly? d) => L(d?.ToString("yyyy-MM-dd") ?? "—");
        // In Arabic documents, keep Latin/number runs such as "2026-2027" in their natural order.
        string L(string s) => rtl ? "‎" + s + "‎" : s;
        // Free text without Arabic letters (e.g. English comments) is laid out left to right.
        IContainer Dir(IContainer c, string? text) => rtl && !string.IsNullOrEmpty(text) && !text.Any(ch => ch is >= '؀' and <= 'ۿ') ? c.ContentFromLeftToRight() : c;

        var brand = Color.FromHex("#0284c7");

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(x => x.FontSize(9.5f).FontFamily(Fonts));
            if (rtl) page.ContentFromRightToLeft();

            page.Header().PaddingBottom(10).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(card.SchoolName).FontSize(15).Bold().FontColor(brand);
                    c.Item().Text($"{t["year"]}: {L(card.AcademicYearName)}").FontColor(Colors.Grey.Darken1);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text(t["title"]).FontSize(18).Bold();
                    c.Item().AlignRight().Text($"{t["term"]}: {L(card.TermName)}").FontSize(11);
                    if (card.Status != Shared.Enums.ReportCardStatus.Published)
                        c.Item().AlignRight().Text(t["draft"]).FontColor(Colors.Red.Medium).Bold();
                });
            });

            page.Content().Column(col =>
            {
                col.Spacing(10);

                col.Item().Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Row(row =>
                {
                    void Field(RowDescriptor r, string label, string value) => r.RelativeItem().Column(c =>
                    {
                        c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                        c.Item().Text(value).Bold();
                    });
                    Field(row, t["student"], card.StudentName);
                    Field(row, t["no"], card.StudentNo);
                    Field(row, t["dob"], D(card.DateOfBirth));
                    Field(row, t["class"], card.ClassName);
                    Field(row, t["homeroom"], card.HomeroomTeacher ?? "—");
                });

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2.2f);
                        c.ConstantColumn(38);
                        c.ConstantColumn(58);
                        c.ConstantColumn(52);
                        c.RelativeColumn(1.6f);
                        c.RelativeColumn(3);
                    });

                    IContainer Head(IContainer x) => x.Background(brand).PaddingVertical(5).PaddingHorizontal(4);
                    IContainer Cell(IContainer x) => x.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).PaddingHorizontal(4);

                    table.Header(h =>
                    {
                        foreach (var key in new[] { "subject", "coef", "avg", "classAvg", "teacher", "comment" })
                            h.Cell().Element(Head).Text(t[key]).FontColor(Colors.White).Bold().FontSize(8.5f);
                    });

                    foreach (var s in card.Subjects)
                    {
                        table.Cell().Element(Cell).Text(s.SubjectName).SemiBold();
                        table.Cell().Element(Cell).AlignCenter().Text(s.Coefficient.ToString("0.#", CultureInfo.InvariantCulture));
                        table.Cell().Element(Cell).AlignCenter().Text(N(s.Average)).Bold()
                            .FontColor(s.Average is < 10 ? Colors.Red.Darken1 : Colors.Black);
                        table.Cell().Element(Cell).AlignCenter().Text(N(s.ClassAverage)).FontColor(Colors.Grey.Darken1);
                        table.Cell().Element(Cell).Text(s.TeacherName ?? "—").FontSize(8.5f);
                        table.Cell().Element(Cell).Element(x => Dir(x, s.Comment)).Text(s.Comment ?? "").FontSize(8.5f).Italic();
                    }
                });

                col.Item().Row(row =>
                {
                    row.RelativeItem().Border(0.75f).BorderColor(brand).Background(Color.FromHex("#f0f9ff")).Padding(10).Column(c =>
                    {
                        c.Item().Text(t["overall"]).FontColor(Colors.Grey.Darken2);
                        c.Item().Text($"{N(card.Average)} / 20").FontSize(20).Bold().FontColor(brand);
                        c.Item().Text($"{t["rank"]}: {(card.Rank?.ToString() ?? "—")} {t["of"]} {card.ClassSize}   ·   {t["classAvg"]}: {N(card.ClassAverage)}");
                    });
                    row.ConstantItem(12);
                    row.RelativeItem().Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(c =>
                    {
                        c.Item().Text(t["attendance"]).Bold();
                        c.Item().Text($"{t["absences"]}: {card.AbsenceDays}");
                        c.Item().Text($"{t["unjustified"]}: {card.UnjustifiedAbsences}");
                        c.Item().Text($"{t["late"]}: {card.LateCount}");
                    });
                });

                col.Item().Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(10).MinHeight(60).Column(c =>
                {
                    c.Item().Text(t["general"]).Bold();
                    c.Item().PaddingTop(4).Element(x => Dir(x, card.GeneralComment)).Text(card.GeneralComment ?? "");
                });

                col.Item().PaddingTop(16).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(t["signature"]).FontColor(Colors.Grey.Darken1);
                        c.Item().PaddingTop(28).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                    });
                    row.ConstantItem(40);
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(t["parent"]).FontColor(Colors.Grey.Darken1);
                        c.Item().PaddingTop(28).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                    });
                });
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text($"{t["generated"]} {DateTime.Now.ToString("d", culture)}").FontSize(8).FontColor(Colors.Grey.Medium);
                row.RelativeItem().AlignRight().Text(x =>
                {
                    x.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Medium));
                    x.Span($"{t["page"]} ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        })).GeneratePdf();
    }
}
