using MasterApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MasterApi.Data;

public sealed class MasterDbInitializer(MasterDbContext db)
{
    private static readonly Guid GameAdministrationLaunchEntryId = Guid.Parse("4f31a5d8-4fdf-4d98-9f35-3d8d4f4e5c10");
    private static readonly Guid DirectionalLinksEntryId = Guid.Parse("1a6641d2-4e89-4087-81c5-bbcd0f45ca31");
    private static readonly Guid ManufacturingProductSelectorEntryId = Guid.Parse("a3d807fa-cdd9-4977-ae3a-7e922e9b0755");
    private static readonly Guid NewspaperChangelogRestoredEntryId = Guid.Parse("b8e2f1c3-5a7d-4e9b-8c6f-2d1a3b0e4f5c");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            await EnsureLegacyGameNewsSchemaAsync(cancellationToken);
        }

        await SeedChangelogEntryAsync(
            GameAdministrationLaunchEntryId,
            new DateTime(2026, 4, 10, 8, 0, 0, DateTimeKind.Utc),
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "en",
                Title = "Game administration and newsroom launched",
                Summary = "The game now ships a shared newspaper, changelog feed, unread tracking, and a live admin operations dashboard.",
                HtmlContent = "<p>The shared newsroom is now live in every game shard.</p><ul><li>Published news and changelog posts now come from MasterApi.</li><li>Players see unread counts directly in the navbar.</li><li>Administrators can review anomalies, manage roles, toggle invisible support mode, and impersonate accounts with audit logs.</li></ul>",
            },
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "sk",
                Title = "Správa hry a newsroom sú spustené",
                Summary = "Hra teraz obsahuje zdieľané noviny, changelog feed, sledovanie neprečítaných správ a živý administračný dashboard.",
                HtmlContent = "<p>Zdieľaný newsroom je teraz aktívny na každom hernom serveri.</p><ul><li>Publikované novinky a changelog sa načítavajú z MasterApi.</li><li>Hráči vidia počet neprečítaných správ priamo v navigácii.</li><li>Administrátori môžu sledovať anomálie, spravovať roly, prepínať neviditeľný režim podpory a impersonovať účty s audit logom.</li></ul>",
            },
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "de",
                Title = "Spieladministration und Newsroom gestartet",
                Summary = "Das Spiel enthält jetzt eine gemeinsame Zeitung, einen Changelog-Feed, ungelesene Hinweise in der Navigation und ein Live-Admin-Dashboard.",
                HtmlContent = "<p>Der gemeinsame Newsroom ist jetzt auf jedem Spielserver aktiv.</p><ul><li>Veröffentlichte News und Changelog-Einträge kommen jetzt aus der MasterApi.</li><li>Spieler sehen die Anzahl ungelesener Meldungen direkt in der Navigation.</li><li>Administratoren können Auffälligkeiten prüfen, Rollen verwalten, den unsichtbaren Support-Modus schalten und Konten mit Audit-Log impersonieren.</li></ul>",
            },
            cancellationToken);

        await SeedChangelogEntryAsync(
            DirectionalLinksEntryId,
            new DateTime(2026, 4, 12, 18, 45, 0, DateTimeKind.Utc),
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "en",
                Title = "Building unit directional links with diagonal support",
                Summary = "Unit connections in factories and mines now support all 8 directional link types including diagonals, with visible direction arrows in the grid.",
                HtmlContent = "<p>The building configuration editor now supports full directional unit links.</p><ul><li>Horizontal, vertical, and all four diagonal link directions are available.</li><li>Link direction arrows are shown directly in the 4x4 grid so you can trace the resource flow at a glance.</li><li>The link cycle button steps through none, forward, backward, and diagonal states.</li></ul>",
            },
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "sk",
                Title = "Smerové prepojenia stavebných jednotiek s uhlopriečnou podporou",
                Summary = "Prepojenia jednotiek v továrňach a baniach teraz podporujú všetkých 8 smerových typov vrátane uhlopriečnych, so viditeľnými šípkami smeru v mriežke.",
                HtmlContent = "<p>Editor konfigurácie budov teraz podporuje plné smerové prepojenia jednotiek.</p><ul><li>Sú k dispozícii horizontálne, vertikálne a všetky štyri uhlopriečne smery prepojenia.</li><li>Šípky smeru prepojenia sú zobrazené priamo v mriežke 4x4, takže tok zdrojov vidíte na prvý pohľad.</li><li>Tlačidlo cyklu prepojenia prechádza stavmi: žiadne, dopredu, dozadu a uhlopriečne.</li></ul>",
            },
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "de",
                Title = "Direktionale Gebäudeeinheitsverknüpfungen mit Diagonalunterstützung",
                Summary = "Einheitsverbindungen in Fabriken und Minen unterstützen jetzt alle 8 Richtungstypen einschließlich Diagonalen mit sichtbaren Richtungspfeilen im Raster.",
                HtmlContent = "<p>Der Gebäudekonfigurations-Editor unterstützt jetzt vollständige direktionale Einheitsverknüpfungen.</p><ul><li>Horizontale, vertikale und alle vier diagonalen Verknüpfungsrichtungen stehen zur Verfügung.</li><li>Richtungspfeile werden direkt im 4x4-Raster angezeigt, sodass Sie den Ressourcenfluss auf einen Blick verfolgen können.</li><li>Der Zyklus-Knopf wechselt zwischen den Zuständen: keiner, vorwärts, rückwärts und diagonal.</li></ul>",
            },
            cancellationToken);

        await SeedChangelogEntryAsync(
            ManufacturingProductSelectorEntryId,
            new DateTime(2026, 4, 12, 19, 50, 0, DateTimeKind.Utc),
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "en",
                Title = "Manufacturing output product selector shows product images",
                Summary = "The manufacturing unit's output product selector now displays product images alongside names for faster visual identification.",
                HtmlContent = "<p>Configuring your factory's output product is now more intuitive.</p><ul><li>The product selector in manufacturing units now shows the product image next to the name.</li><li>Quickly identify Wooden Chair, Bread, Medicine, and other products at a glance.</li><li>The image tiles make it easier to scan all available options without reading every name.</li></ul>",
            },
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "sk",
                Title = "Selektor výstupného produktu výroby zobrazuje obrázky produktov",
                Summary = "Selektor výstupného produktu výrobnej jednotky teraz zobrazuje obrázky produktov vedľa názvov pre rýchlejšiu vizuálnu identifikáciu.",
                HtmlContent = "<p>Konfigurácia výstupného produktu vašej továrne je teraz intuitívnejšia.</p><ul><li>Selektor produktov vo výrobných jednotkách teraz zobrazuje obrázok produktu vedľa názvu.</li><li>Rýchlo identifikujte Drevenú stoličku, Chlieb, Lieky a ďalšie produkty na prvý pohľad.</li><li>Obrázkové dlaždice uľahčujú prehľadávanie všetkých dostupných možností bez čítania každého názvu.</li></ul>",
            },
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "de",
                Title = "Ausgabeprodukt-Selektor der Fertigung zeigt Produktbilder",
                Summary = "Der Ausgabeprodukt-Selektor der Fertigungseinheit zeigt jetzt Produktbilder neben den Namen zur schnelleren visuellen Identifikation.",
                HtmlContent = "<p>Die Konfiguration des Ausgabeprodukts Ihrer Fabrik ist jetzt intuitiver.</p><ul><li>Der Produktselektor in Fertigungseinheiten zeigt jetzt das Produktbild neben dem Namen.</li><li>Identifizieren Sie Holzstuhl, Brot, Medizin und andere Produkte auf einen Blick.</li><li>Die Bildkacheln erleichtern das Durchsuchen aller verfügbaren Optionen ohne jeden Namen lesen zu müssen.</li></ul>",
            },
            cancellationToken);

        await SeedChangelogEntryAsync(
            NewspaperChangelogRestoredEntryId,
            new DateTime(2026, 4, 12, 21, 0, 0, DateTimeKind.Utc),
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "en",
                Title = "Newspaper and changelog data flow restored",
                Summary = "The in-game newspaper and changelog now reliably load from the master API and show player-relevant news and product updates.",
                HtmlContent = "<p>The in-game newspaper is back in full operation.</p><ul><li>All published changelog and news entries are now loaded from the master API and displayed in the News section.</li><li>Unread counts in the navbar update correctly when new entries are published.</li><li>Opening the News page marks all visible entries as read and clears the badge.</li><li>Unauthenticated players can browse the changelog without logging in.</li><li>Error and empty states are shown with clear explanations instead of a blank screen.</li></ul>",
            },
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "sk",
                Title = "Tok dát novín a changelogu obnovený",
                Summary = "Herné noviny a changelog sa teraz spoľahlivo načítavajú z hlavného API a zobrazujú relevantné novinky a aktualizácie produktov.",
                HtmlContent = "<p>Herné noviny sú opäť v plnej prevádzke.</p><ul><li>Všetky publikované záznamy changelogu a noviniek sa teraz načítavajú z hlavného API a zobrazujú v sekcii Správy.</li><li>Počet neprečítaných správ v navigácii sa správne aktualizuje pri zverejnení nových záznamov.</li><li>Otvorením stránky Správy sa všetky viditeľné záznamy označia ako prečítané a odznak sa vymaže.</li><li>Neautentifikovaní hráči môžu prehliadať changelog bez prihlásenia.</li><li>Chybové a prázdne stavy sa zobrazujú s jasným vysvetlením namiesto prázdnej obrazovky.</li></ul>",
            },
            new GameNewsEntryLocalization
            {
                Id = Guid.NewGuid(),
                Locale = "de",
                Title = "Datenfluss für Zeitung und Changelog wiederhergestellt",
                Summary = "Die In-Game-Zeitung und der Changelog laden jetzt zuverlässig von der Master-API und zeigen spielerrelevante Neuigkeiten und Produktaktualisierungen.",
                HtmlContent = "<p>Die In-Game-Zeitung ist wieder in vollem Betrieb.</p><ul><li>Alle veröffentlichten Changelog- und Neuigkeitseinträge werden jetzt von der Master-API geladen und im Bereich Nachrichten angezeigt.</li><li>Ungelesene Zählungen in der Navigationsleiste werden korrekt aktualisiert, wenn neue Einträge veröffentlicht werden.</li><li>Durch Öffnen der Nachrichtenseite werden alle sichtbaren Einträge als gelesen markiert und das Abzeichen wird gelöscht.</li><li>Nicht authentifizierte Spieler können den Changelog ohne Anmeldung durchsuchen.</li><li>Fehler- und leere Zustände werden mit klaren Erklärungen anstelle eines leeren Bildschirms angezeigt.</li></ul>",
            },
            cancellationToken);
    }

    private async Task SeedChangelogEntryAsync(
        Guid entryId,
        DateTime publishedAt,
        GameNewsEntryLocalization enLocalization,
        GameNewsEntryLocalization skLocalization,
        GameNewsEntryLocalization deLocalization,
        CancellationToken cancellationToken)
    {
        if (await db.GameNewsEntries.AnyAsync(entry => entry.Id == entryId, cancellationToken))
        {
            return;
        }

        db.GameNewsEntries.Add(new GameNewsEntry
        {
            Id = entryId,
            EntryType = GameNewsEntryType.Changelog,
            Status = GameNewsEntryStatus.Published,
            TargetServerKey = null,
            CreatedByEmail = "system@capitalism.local",
            UpdatedByEmail = "system@capitalism.local",
            CreatedAtUtc = publishedAt,
            UpdatedAtUtc = publishedAt,
            PublishedAtUtc = publishedAt,
            Localizations = [enLocalization, skLocalization, deLocalization],
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureLegacyGameNewsSchemaAsync(CancellationToken cancellationToken)
    {
        foreach (var commandText in GetLegacyGameNewsSchemaCommands())
        {
            await db.Database.ExecuteSqlRawAsync(commandText, cancellationToken);
        }
    }

    private string[] GetLegacyGameNewsSchemaCommands()
    {
        return
        [
            """
            CREATE TABLE IF NOT EXISTS "GameNewsEntries" (
                "Id" uuid NOT NULL,
                "EntryType" character varying(20) NOT NULL,
                "Status" character varying(20) NOT NULL,
                "TargetServerKey" character varying(120),
                "CreatedByEmail" character varying(200) NOT NULL,
                "UpdatedByEmail" character varying(200) NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL,
                "PublishedAtUtc" timestamp with time zone,
                CONSTRAINT "PK_GameNewsEntries" PRIMARY KEY ("Id")
            )
            """,
            """
            CREATE TABLE IF NOT EXISTS "GameNewsEntryLocalizations" (
                "Id" uuid NOT NULL,
                "GameNewsEntryId" uuid NOT NULL,
                "Locale" character varying(10) NOT NULL,
                "Title" character varying(220) NOT NULL,
                "Summary" character varying(1000) NOT NULL,
                "HtmlContent" text NOT NULL,
                CONSTRAINT "PK_GameNewsEntryLocalizations" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_GameNewsEntryLocalizations_GameNewsEntries_GameNewsEntryId"
                    FOREIGN KEY ("GameNewsEntryId") REFERENCES "GameNewsEntries" ("Id") ON DELETE CASCADE
            )
            """,
            """
            CREATE TABLE IF NOT EXISTS "GameNewsReadReceipts" (
                "Id" uuid NOT NULL,
                "GameNewsEntryId" uuid NOT NULL,
                "PlayerEmail" character varying(200) NOT NULL,
                "ServerKey" character varying(120) NOT NULL,
                "ReadAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_GameNewsReadReceipts" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_GameNewsReadReceipts_GameNewsEntries_GameNewsEntryId"
                    FOREIGN KEY ("GameNewsEntryId") REFERENCES "GameNewsEntries" ("Id") ON DELETE CASCADE
            )
            """,
            "CREATE INDEX IF NOT EXISTS \"IX_GameNewsEntries_TargetServerKey_PublishedAtUtc\" ON \"GameNewsEntries\" (\"TargetServerKey\", \"PublishedAtUtc\")",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_GameNewsEntryLocalizations_GameNewsEntryId_Locale\" ON \"GameNewsEntryLocalizations\" (\"GameNewsEntryId\", \"Locale\")",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_GameNewsReadReceipts_GameNewsEntryId_PlayerEmail_ServerKey\" ON \"GameNewsReadReceipts\" (\"GameNewsEntryId\", \"PlayerEmail\", \"ServerKey\")",
        ];
    }
}