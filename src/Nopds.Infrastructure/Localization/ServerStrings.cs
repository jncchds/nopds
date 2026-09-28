using System.Collections.Frozen;
using Nopds.Domain.Text;

namespace Nopds.Infrastructure.Localization;

/// <summary>Server-side UI strings (OPDS feeds, Telegram bot) in en/uk/pl/de.</summary>
public static class ServerStrings
{
    // key → [en, uk, pl, de]
    private static readonly FrozenDictionary<string, string[]> Table = new Dictionary<string, string[]>
    {
        ["catalog"] = ["Catalog", "Каталог", "Katalog", "Katalog"],
        ["libraries"] = ["Libraries", "Бібліотеки", "Biblioteki", "Bibliotheken"],
        ["libraries.desc"] = ["Choose a library", "Оберіть бібліотеку", "Wybierz bibliotekę", "Bibliothek auswählen"],
        ["folders"] = ["By folders", "За каталогами", "Według folderów", "Nach Ordnern"],
        ["folders.desc"] = ["Browse the library folder tree", "Перегляд дерева каталогів", "Przeglądaj drzewo folderów", "Ordnerstruktur durchsuchen"],
        ["titles"] = ["By titles", "За назвами", "Według tytułów", "Nach Titeln"],
        ["titles.desc"] = ["Books in alphabetical order", "Книги за абеткою", "Książki alfabetycznie", "Bücher alphabetisch"],
        ["authors"] = ["By authors", "За авторами", "Według autorów", "Nach Autoren"],
        ["authors.desc"] = ["Authors in alphabetical order", "Автори за абеткою", "Autorzy alfabetycznie", "Autoren alphabetisch"],
        ["series"] = ["By series", "За серіями", "Według serii", "Nach Serien"],
        ["series.desc"] = ["Series in alphabetical order", "Серії за абеткою", "Serie alfabetycznie", "Serien alphabetisch"],
        ["genres"] = ["By genres", "За жанрами", "Według gatunków", "Nach Genres"],
        ["genres.desc"] = ["Books grouped by genre", "Книги за жанрами", "Książki według gatunków", "Bücher nach Genre"],
        ["shelf"] = ["My bookshelf", "Моя книжкова полиця", "Moja półka", "Mein Bücherregal"],
        ["shelf.desc"] = ["Books you downloaded or read", "Книги, які ви завантажили чи читали", "Książki pobrane lub czytane", "Heruntergeladene oder gelesene Bücher"],
        ["recent"] = ["New books", "Нові надходження", "Nowości", "Neuzugänge"],
        ["recent.desc"] = ["Recently added books", "Нещодавно додані книги", "Ostatnio dodane książki", "Kürzlich hinzugefügte Bücher"],
        ["search"] = ["Search", "Пошук", "Szukaj", "Suche"],
        ["search.books"] = ["Search books", "Пошук книг", "Szukaj książek", "Bücher suchen"],
        ["search.authors"] = ["Search authors", "Пошук авторів", "Szukaj autorów", "Autoren suchen"],
        ["search.series"] = ["Search series", "Пошук серій", "Szukaj serii", "Serien suchen"],
        ["search.books.desc"] = ["Titles containing “{0}”", "Назви, що містять «{0}»", "Tytuły zawierające „{0}”", "Titel mit „{0}“"],
        ["search.authors.desc"] = ["Authors whose name contains “{0}”", "Автори, ім'я яких містить «{0}»", "Autorzy zawierający „{0}”", "Autoren mit „{0}“"],
        ["search.series.desc"] = ["Series containing “{0}”", "Серії, що містять «{0}»", "Serie zawierające „{0}”", "Serien mit „{0}“"],
        ["found.books"] = ["Books found", "Знайдено книги", "Znalezione książki", "Gefundene Bücher"],
        ["found.authors"] = ["Authors found", "Знайдено авторів", "Znalezieni autorzy", "Gefundene Autoren"],
        ["found.series"] = ["Series found", "Знайдено серії", "Znalezione serie", "Gefundene Serien"],
        ["nothing"] = ["Nothing found", "Нічого не знайдено", "Nic nie znaleziono", "Nichts gefunden"],
        ["lang.0"] = ["All", "Усі", "Wszystkie", "Alle"],
        ["lang.1"] = ["Cyrillic", "Кирилиця", "Cyrylica", "Kyrillisch"],
        ["lang.2"] = ["Latin", "Латиниця", "Alfabet łaciński", "Lateinisch"],
        ["lang.3"] = ["Digits", "Цифри", "Cyfry", "Ziffern"],
        ["lang.9"] = ["Other symbols", "Інші символи", "Inne znaki", "Andere Zeichen"],
        ["author.all"] = ["All books", "Усі книги", "Wszystkie książki", "Alle Bücher"],
        ["author.series"] = ["Books by series", "Книги за серіями", "Książki według serii", "Bücher nach Serien"],
        ["author.noseries"] = ["Books outside series", "Книги поза серіями", "Książki spoza serii", "Bücher ohne Serie"],
        ["author.books"] = ["Books by {0}", "Книги автора {0}", "Książki: {0}", "Bücher von {0}"],
        ["series.books"] = ["Series: {0}", "Серія: {0}", "Seria: {0}", "Serie: {0}"],
        ["genre.books"] = ["Genre: {0}", "Жанр: {0}", "Gatunek: {0}", "Genre: {0}"],
        ["editions"] = ["Other editions ({0})", "Інші видання ({0})", "Inne wydania ({0})", "Andere Ausgaben ({0})"],
        ["editions.title"] = ["Editions", "Видання", "Wydania", "Ausgaben"],
        ["dupes.hide"] = ["Hide duplicates", "Приховати дублікати", "Ukryj duplikaty", "Duplikate ausblenden"],
        ["dupes.show"] = ["Show all editions", "Показати всі видання", "Pokaż wszystkie wydania", "Alle Ausgaben zeigen"],
        ["books.count"] = ["{0} books", "Книг: {0}", "Książek: {0}", "{0} Bücher"],
        ["format"] = ["Format", "Формат", "Format", "Format"],
        ["size"] = ["Size", "Розмір", "Rozmiar", "Größe"],
        ["author"] = ["Author", "Автор", "Autor", "Autor"],
        ["language"] = ["Language", "Мова", "Język", "Sprache"],
        ["date"] = ["Date", "Дата", "Data", "Datum"],
        ["genre"] = ["Genre", "Жанр", "Gatunek", "Genre"],
        ["download"] = ["Download", "Завантажити", "Pobierz", "Herunterladen"],
        ["download.zip"] = ["Download {0} (zip)", "Завантажити {0} (zip)", "Pobierz {0} (zip)", "{0} herunterladen (zip)"],
        ["download.as"] = ["Download as {0}", "Завантажити як {0}", "Pobierz jako {0}", "Als {0} herunterladen"],
        ["next"] = ["Next page", "Наступна сторінка", "Następna strona", "Nächste Seite"],
        ["prev"] = ["Previous page", "Попередня сторінка", "Poprzednia strona", "Vorherige Seite"],
        ["unknown.author"] = ["Unknown author", "Невідомий автор", "Nieznany autor", "Unbekannter Autor"],
        ["tg.welcome"] = [
            "Hi! Send me a book title, an author or a series name and I'll search the library.\n/authors <name> — search authors\n/series <name> — search series\n/books <title> — search books",
            "Привіт! Надішліть назву книги, ім'я автора чи назву серії — і я пошукаю в бібліотеці.\n/authors <ім'я> — пошук авторів\n/series <назва> — пошук серій\n/books <назва> — пошук книг",
            "Cześć! Wyślij tytuł książki, autora lub serię, a przeszukam bibliotekę.\n/authors <nazwisko> — szukaj autorów\n/series <nazwa> — szukaj serii\n/books <tytuł> — szukaj książek",
            "Hallo! Schick mir einen Buchtitel, Autor oder Seriennamen und ich durchsuche die Bibliothek.\n/authors <Name> — Autoren suchen\n/series <Name> — Serien suchen\n/books <Titel> — Bücher suchen"],
        ["tg.denied"] = [
            "Access denied. Link your Telegram username in your profile first.",
            "Доступ заборонено. Спочатку вкажіть свій Telegram у профілі.",
            "Brak dostępu. Najpierw powiąż nazwę Telegram w profilu.",
            "Zugriff verweigert. Verknüpfe zuerst deinen Telegram-Namen im Profil."],
        ["tg.choose"] = ["What should I search for “{0}”?", "Що шукати за запитом «{0}»?", "Czego szukać dla „{0}”?", "Wonach soll ich für „{0}“ suchen?"],
        ["tg.books"] = ["Books", "Книги", "Książki", "Bücher"],
        ["tg.authors"] = ["Authors", "Автори", "Autorzy", "Autoren"],
        ["tg.series"] = ["Series", "Серії", "Serie", "Serien"],
        ["tg.more"] = ["More »", "Далі »", "Więcej »", "Mehr »"],
        ["tg.sending"] = ["Sending…", "Надсилаю…", "Wysyłam…", "Sende…"],
        ["tg.toolarge"] = ["The file is too large for Telegram.", "Файл завеликий для Telegram.", "Plik jest za duży dla Telegrama.", "Die Datei ist zu groß für Telegram."],
        ["tg.error"] = ["Sorry, something went wrong.", "Вибачте, сталася помилка.", "Przepraszamy, wystąpił błąd.", "Entschuldigung, etwas ist schiefgelaufen."],
    }.ToFrozenDictionary();

    public static string Get(string lang, string key)
    {
        if (!Table.TryGetValue(key, out var values))
        {
            return key;
        }

        return (UiLanguages.Match(lang) ?? UiLanguages.Default) switch
        {
            "uk" => values[1],
            "pl" => values[2],
            "de" => values[3],
            _ => values[0],
        };
    }

    public static string Format(string lang, string key, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(lang, key), args);
}
