-- Released fork schema v32, archived from ebdac88; no player data.
-- Independent migration fixture: do not generate from the current migration list.

CREATE TABLE accounts (
    id          TEXT PRIMARY KEY,                   -- Guid
    level       INTEGER NOT NULL                    -- AccountLevel: 0 Player, 1 GameMaster, 2 Admin
);
CREATE TABLE characters (
    id                  TEXT PRIMARY KEY,           -- Guid (Join key 1)
    account             TEXT NOT NULL REFERENCES accounts (id),
    name                TEXT NOT NULL UNIQUE COLLATE NOCASE,
    template            INTEGER NOT NULL,           -- CreateCharacter h8
    appearance          INTEGER NOT NULL,           -- CreateCharacter h9
    customization       TEXT NOT NULL,              -- JSON int array (CreateCharacter ia)
    clothes             TEXT NOT NULL,              -- JSON int array (CreateCharacter ib)
    cluster             TEXT NOT NULL,              -- world.xml cluster id
    position_x          REAL NOT NULL,
    position_y          REAL NOT NULL,
    created             INTEGER NOT NULL,           -- UTC DateTime ticks
    equipment_container TEXT NOT NULL,              -- Guid (Join key 51)
    inventory_container TEXT NOT NULL               -- Guid (Join key 54)
);
CREATE TABLE items (
    character   TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    container   INTEGER NOT NULL CHECK (container IN (0, 1)), -- 0 equipment (slot = EquipmentSlot), 1 inventory
    slot        INTEGER NOT NULL,                   -- may lie beyond the container (kept, not sent)
    uniquename  TEXT NOT NULL,
    count       INTEGER NOT NULL,
    quality     INTEGER NOT NULL,
    durability  INTEGER,                            -- a7m raw; NULL = full
    active      TEXT,                               -- JSON array, spell uniquename per slot (null = none); NULL = never chosen
    passive     TEXT,
    PRIMARY KEY (character, container, slot)
) WITHOUT ROWID;
CREATE TABLE progression (                          -- no row = everything unlocked (Character.Progression null)
    character   TEXT PRIMARY KEY REFERENCES characters (id) ON DELETE CASCADE,
    completed   TEXT NOT NULL,                      -- JSON int array in completion order (FullAchievementInfo key 1)
    levels      TEXT NOT NULL                       -- JSON object: achievement id -> level
);

ALTER TABLE characters ADD COLUMN direction REAL NOT NULL DEFAULT 0;            -- degrees (Join key 10)
ALTER TABLE characters ADD COLUMN previous_cluster TEXT NOT NULL DEFAULT '';    -- Join key 66
ALTER TABLE characters ADD COLUMN last_left_at INTEGER NOT NULL DEFAULT 0;      -- UTC ticks (Join key 101)
ALTER TABLE characters ADD COLUMN last_city TEXT NOT NULL DEFAULT '';           -- Join key 118
ALTER TABLE characters ADD COLUMN pending_transfer TEXT;                        -- JSON PendingTransfer; NULL = none

ALTER TABLE accounts ADD COLUMN name TEXT COLLATE NOCASE;   -- login name (Login key 6); NULL = the local account, not claimed yet
CREATE UNIQUE INDEX accounts_name ON accounts (name);

ALTER TABLE characters ADD COLUMN fame INTEGER NOT NULL DEFAULT 0;              -- a7m raw (x10000), UpdateFame key 1

ALTER TABLE accounts ADD COLUMN display_name TEXT;                              -- the name as typed (Login key 64 of refusals)
ALTER TABLE accounts ADD COLUMN password TEXT;                                  -- Passwords.Hash form; NULL = none yet
ALTER TABLE accounts ADD COLUMN failed_logins INTEGER NOT NULL DEFAULT 0;       -- wrong passwords in a row
ALTER TABLE accounts ADD COLUMN locked_until INTEGER;                           -- UTC ticks; NULL = not locked
ALTER TABLE accounts ADD COLUMN banned_until INTEGER;                           -- UTC ticks; NULL = not banned
ALTER TABLE accounts ADD COLUMN ban_reason TEXT NOT NULL DEFAULT '';
ALTER TABLE accounts ADD COLUMN created INTEGER NOT NULL DEFAULT 0;             -- UTC ticks
UPDATE accounts SET display_name = name, name = lower(name),
    created = strftime('%s', 'now') * 10000000 + 621355968000000000;

ALTER TABLE progression ADD COLUMN progress TEXT NOT NULL DEFAULT '{}';         -- JSON object: achievement id -> a7m raw

ALTER TABLE characters ADD COLUMN silver INTEGER NOT NULL DEFAULT 0;            -- a7m raw (Join key 33)
ALTER TABLE characters ADD COLUMN gold INTEGER NOT NULL DEFAULT 0;              -- a7m raw (Join key 34)
CREATE TABLE bank_items (
    character   TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    location    TEXT NOT NULL,                      -- the bank's cluster id
    tab         TEXT NOT NULL,                      -- vault tab Guid (bgf.u0; tab 0 = 7deb25d2-...)
    slot        INTEGER NOT NULL,
    uniquename  TEXT NOT NULL,
    count       INTEGER NOT NULL,
    quality     INTEGER NOT NULL,
    durability  INTEGER,
    active      TEXT,
    passive     TEXT,
    PRIMARY KEY (character, location, tab, slot)
) WITHOUT ROWID;
CREATE TABLE market_orders (
    id          INTEGER PRIMARY KEY,                -- AuctionDTO Id (from 1000000000)
    owner       TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    type        TEXT NOT NULL,                      -- 'offer' (sell order) or 'request' (buy order)
    location    TEXT NOT NULL,                      -- the marketplace's cluster id
    uniquename  TEXT NOT NULL,                      -- ItemTypeId (T4_X@n)
    quality     INTEGER NOT NULL,
    amount      INTEGER NOT NULL,                   -- still open
    unit_price  INTEGER NOT NULL,                   -- a7m raw
    created     INTEGER NOT NULL,                   -- UTC ticks
    expires     INTEGER NOT NULL,                   -- UTC ticks
    reference   TEXT NOT NULL,                      -- ReferenceId Guid
    item        TEXT                                -- offer: JSON of the stack it holds (durability, spells)
);
CREATE TABLE market_finished (
    id          INTEGER PRIMARY KEY,
    owner       TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    order_id    INTEGER NOT NULL,                   -- the order it came from (AuctionDTO Id)
    type        TEXT NOT NULL,
    location    TEXT NOT NULL,
    uniquename  TEXT NOT NULL,
    quality     INTEGER NOT NULL,
    amount      INTEGER NOT NULL,                   -- items to collect
    unit_price  INTEGER NOT NULL,
    silver      INTEGER NOT NULL,                   -- a7m raw to collect
    created     INTEGER NOT NULL,
    expires     INTEGER NOT NULL,
    reference   TEXT NOT NULL,
    item        TEXT
);
CREATE TABLE market_credits (
    character   TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    silver      INTEGER NOT NULL                    -- a7m raw, added to the character at its next Join
);

ALTER TABLE characters ADD COLUMN effects TEXT NOT NULL DEFAULT '[]';          -- JSON array of SavedEffect
ALTER TABLE characters ADD COLUMN cooldowns TEXT NOT NULL DEFAULT '[]';        -- JSON array of SavedCooldown
ALTER TABLE characters ADD COLUMN focus REAL NOT NULL DEFAULT 0;              -- crafting focus points

ALTER TABLE characters ADD COLUMN mounted_since INTEGER NOT NULL DEFAULT 0;   -- GameTimeStamp of the mount, 0 = dismounted
ALTER TABLE characters ADD COLUMN mount_cooldown INTEGER NOT NULL DEFAULT 0;  -- GameTimeStamp it may mount again from

ALTER TABLE characters ADD COLUMN flag INTEGER NOT NULL DEFAULT 0;           -- FlaggingStatus: 0 Blue, 1-6 a faction, 255 Red
ALTER TABLE characters ADD COLUMN faction INTEGER NOT NULL DEFAULT 0;        -- the enlisted faction (a FlaggingStatus), 0 = none
ALTER TABLE characters ADD COLUMN reputation REAL NOT NULL DEFAULT 0;        -- reputation points (-50000 â€¦ 50000)
ALTER TABLE characters ADD COLUMN reputation_at INTEGER NOT NULL DEFAULT 0;  -- UTC ticks of the last recovery step, 0 = never

ALTER TABLE characters ADD COLUMN dungeon_return TEXT;                       -- JSON DungeonReturn; NULL = none

ALTER TABLE characters ADD COLUMN journal_tracked TEXT;                      -- JSON string[]; NULL = never set (suggestions)

ALTER TABLE items ADD COLUMN fame INTEGER;                                   -- a7m raw; NULL = 0 (_EMPTY) or maxfame (_FULL)
ALTER TABLE bank_items ADD COLUMN fame INTEGER;

ALTER TABLE characters ADD COLUMN expeditions TEXT;                          -- JSON ExpeditionLog; NULL = none

ALTER TABLE characters ADD COLUMN goals TEXT;                                -- JSON GoalLog; NULL = none

ALTER TABLE characters ADD COLUMN black_market TEXT;                         -- JSON BlackMarketLog; NULL = none

CREATE TABLE guilds (
    id          TEXT PRIMARY KEY,                   -- Guid (GuildUpdate key 0)
    name        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    created     INTEGER NOT NULL,                   -- UTC ticks
    logo        TEXT NOT NULL,                      -- JSON GuildLogo (CreateGuild keys 1-7)
    banner      TEXT NOT NULL,                      -- CreateGuild key 8
    silver      INTEGER NOT NULL DEFAULT 0,         -- a7m raw (GuildUpdate key 11)
    tax         INTEGER NOT NULL DEFAULT 0,         -- a7m raw (key 14)
    xp          INTEGER NOT NULL DEFAULT 0,         -- guild experience (SPEC Â§10)
    tags        TEXT NOT NULL                       -- JSON Guid[8], the role tag ids (GuildRoles.TagRoles order)
);
CREATE TABLE guild_members (
    character   TEXT PRIMARY KEY REFERENCES characters (id) ON DELETE CASCADE,
    guild       TEXT NOT NULL REFERENCES guilds (id) ON DELETE CASCADE,
    role        INTEGER NOT NULL,                   -- GuildRole 1-8
    joined      INTEGER NOT NULL,                   -- UTC ticks
    last_seen   INTEGER NOT NULL,                   -- UTC ticks
    position    INTEGER NOT NULL                    -- join order
);
CREATE INDEX guild_members_guild ON guild_members (guild);
CREATE TABLE guild_left (
    character   TEXT PRIMARY KEY REFERENCES characters (id) ON DELETE CASCADE,
    left_at     INTEGER NOT NULL                    -- UTC ticks
);

ALTER TABLE guilds ADD COLUMN contracts TEXT;                                -- JSON GuildContracts; NULL = none rolled

CREATE TABLE islands (
    id            TEXT PRIMARY KEY,                 -- Guid (instance "@ISLAND@<id>", GetIslandInfos key 0)
    owner         TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    island_type   INTEGER NOT NULL,                 -- worldsettings.xml island, 1-based (key 2)
    level         INTEGER NOT NULL,                 -- 0-5 (key 6)
    access_point  TEXT NOT NULL,                    -- "<islandaccesspoint id>@<city>" (key 1)
    bought        INTEGER NOT NULL,                 -- UTC ticks (key 8)
    level_changed INTEGER NOT NULL DEFAULT 0,       -- UTC ticks, 0 = never (key 7)
    favorite      INTEGER NOT NULL DEFAULT 0,
    hidden        INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX islands_owner ON islands (owner);

CREATE TABLE island_buildings (
    id            TEXT PRIMARY KEY,                 -- Guid (NewBuilding key 1)
    island        TEXT NOT NULL REFERENCES islands (id) ON DELETE CASCADE,
    name          TEXT NOT NULL,                    -- buildings.xml uniquename (key 3)
    x             REAL NOT NULL,
    z             REAL NOT NULL,
    rotation      REAL NOT NULL DEFAULT 0,
    built         INTEGER NOT NULL                  -- UTC ticks (key 8)
);
CREATE INDEX island_buildings_island ON island_buildings (island);
CREATE TABLE island_farmables (
    id            TEXT PRIMARY KEY,                 -- Guid (NewBuilding key 1)
    island        TEXT NOT NULL REFERENCES islands (id) ON DELETE CASCADE,
    building      TEXT NOT NULL REFERENCES island_buildings (id) ON DELETE CASCADE,
    item          TEXT NOT NULL,                    -- the seed's items.xml uniquename (key 3)
    x             REAL NOT NULL,
    z             REAL NOT NULL,
    planted       INTEGER NOT NULL,                 -- GameTimeStamp (UTC ticks)
    boosts        INTEGER NOT NULL DEFAULT 0,       -- focus waterings (FarmableObjectInfo key 14)
    boosted_at    INTEGER NOT NULL                  -- GameTimeStamp of the last, long.MinValue = never (key 13)
);
CREATE INDEX island_farmables_island ON island_farmables (island);

CREATE TABLE friends (
    a             TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE, -- the smaller Guid string of the pair
    b             TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    since         INTEGER NOT NULL,                 -- UTC ticks
    PRIMARY KEY (a, b)
);
CREATE INDEX friends_b ON friends (b);
CREATE TABLE friend_requests (
    from_char     TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    to_char       TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    created       INTEGER NOT NULL,                 -- UTC ticks
    PRIMARY KEY (from_char, to_char)
);
CREATE INDEX friend_requests_to ON friend_requests (to_char);
CREATE TABLE mutes (
    owner         TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    muted         TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    muted_name    TEXT NOT NULL,                    -- MutePlayerUpdate key 3
    at            INTEGER NOT NULL,                 -- UTC ticks (key 5)
    PRIMARY KEY (owner, muted)
);
CREATE INDEX mutes_muted ON mutes (muted);

CREATE TABLE mails (
    id            INTEGER PRIMARY KEY AUTOINCREMENT, -- from 1000000, never reused (GetMailInfos key 3; DeleteMail's upper bound)
    character     TEXT REFERENCES characters (id) ON DELETE CASCADE, -- a personal mailbox's owner, or NULL
    guild         TEXT REFERENCES guilds (id) ON DELETE CASCADE,     -- a guild mailbox's owner, or NULL
    kind          INTEGER NOT NULL,                 -- MailKind (bd3.a)
    category      INTEGER NOT NULL,                 -- MailCategory (bd3.b) 1-6
    sender        TEXT NOT NULL,                    -- a name or a cluster id (key 7)
    sender_id     TEXT,                             -- the sending character's Guid (key 4); NULL = none
    from_guild    INTEGER NOT NULL DEFAULT 0,
    subject       TEXT NOT NULL,                    -- key 11
    body          TEXT NOT NULL,                    -- ReadMail key 1
    editor        TEXT,                             -- ReadMail key 2
    received      INTEGER NOT NULL,                 -- UTC ticks (key 12)
    expires       INTEGER,                          -- UTC ticks; NULL = never
    read          INTEGER NOT NULL DEFAULT 0,
    CHECK ((character IS NULL) <> (guild IS NULL))
);
INSERT INTO sqlite_sequence (name, seq) VALUES ('mails', 999999);
CREATE INDEX mails_character ON mails (character, id DESC);
CREATE INDEX mails_guild ON mails (guild, id DESC);
CREATE TABLE mail_attachments (
    id            TEXT PRIMARY KEY,                 -- Guid (ReadMail key 3, ClaimAttachmentFromMail key 2)
    mail          INTEGER NOT NULL REFERENCES mails (id) ON DELETE CASCADE,
    item          TEXT NOT NULL                     -- JSON InventoryItem (as market_orders.item)
);
CREATE INDEX mail_attachments_mail ON mail_attachments (mail);
CREATE TABLE mail_sends (
    character     TEXT NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    at            INTEGER NOT NULL                  -- UTC ticks; Mail:DailyLimit counts those since 00:00 UTC
);
CREATE INDEX mail_sends_character ON mail_sends (character, at);
ALTER TABLE market_orders ADD COLUMN initial_amount INTEGER;                 -- the amount placed; NULL (older orders) = amount

ALTER TABLE characters ADD COLUMN focus_stamp INTEGER NOT NULL DEFAULT 0;     -- GameTimeStamp of the focus value, 0 = never
ALTER TABLE characters ADD COLUMN learning_points INTEGER NOT NULL DEFAULT 0; -- a7m raw
ALTER TABLE characters ADD COLUMN lp_stamp INTEGER NOT NULL DEFAULT 0;        -- GameTimeStamp of the last point paid, 0 = never
ALTER TABLE characters ADD COLUMN use_flags INTEGER NOT NULL DEFAULT 3;       -- UseFlag bits (0 learning points, 1 focus, 2 respec silver)
ALTER TABLE characters ADD COLUMN autolearn TEXT;                             -- JSON AutoLearnSet; NULL = none

ALTER TABLE characters ADD COLUMN infamy TEXT;                                -- JSON InfamyLog; NULL = none

ALTER TABLE guilds ADD COLUMN progress TEXT;                                 -- JSON GuildProgress; NULL = none yet

CREATE TABLE road_links (
    id            INTEGER PRIMARY KEY,              -- the network's own number
    out_cluster   TEXT NOT NULL,                    -- world.xml cluster of the outgoing spot (whose lifetime the link has)
    out_path      TEXT NOT NULL,                    -- its criticalObjects path (instanceslot_00/tunnelexit_01)
    in_cluster    TEXT NOT NULL,
    in_path       TEXT NOT NULL,
    type          INTEGER NOT NULL,                 -- tunnelexittype index (SOLO 0, GROUP 1, RAID 2; NewTunnelExit key 5)
    opened        INTEGER NOT NULL,                 -- key 7
    closes        INTEGER NOT NULL,                 -- key 8
    charges       INTEGER NOT NULL,                 -- key 9
    next_charge   INTEGER NOT NULL DEFAULT 0,       -- key 11; 0 = full
    recent_until  INTEGER NOT NULL DEFAULT 0        -- key 12; 0 = never used
);
CREATE TABLE road_slots (
    cluster       TEXT NOT NULL,
    path          TEXT NOT NULL,
    state         INTEGER NOT NULL,                 -- 1 searching (with its keyword), 2 idle
    keyword       TEXT,
    closes        INTEGER NOT NULL,                 -- the end of its lifetime: then it rolls again
    PRIMARY KEY (cluster, path)
);

ALTER TABLE characters ADD COLUMN mist_standing INTEGER NOT NULL DEFAULT 0;   -- a7m raw (UpdateStanding 87 key 1)
ALTER TABLE characters ADD COLUMN mist_favor INTEGER NOT NULL DEFAULT 0;      -- a7m raw (the trader's FAVOR currency)

ALTER TABLE island_farmables ADD COLUMN nutrition REAL NOT NULL DEFAULT 0;    -- food units left at fed_at
ALTER TABLE island_farmables ADD COLUMN fed_at INTEGER NOT NULL DEFAULT 0;    -- GameTimeStamp of nutrition and growth (0 = planted)
ALTER TABLE island_farmables ADD COLUMN growth INTEGER NOT NULL DEFAULT 0;    -- ticks grown (a baby) or produced (a grown animal) at fed_at

ALTER TABLE characters ADD COLUMN loadouts TEXT;                             -- JSON Loadout[]; NULL = none

ALTER TABLE characters ADD COLUMN played_ms INTEGER NOT NULL DEFAULT 0 CHECK (played_ms >= 0);

ALTER TABLE characters ADD COLUMN tutorial_opening TEXT;

ALTER TABLE progression ADD COLUMN tracked INTEGER;
PRAGMA user_version = 32;
