using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SimpleArenaPvP", "stoneharry", "0.1")]
    public class SimpleArenaPvP : RustPlugin
    {
        private static Game game = new Game();

        #region Hooks
        bool? OnPlayerLand(BasePlayer player)
        {
            if (game.HasPlayer(player.name))
                return true;
            return null;
        }

        bool? OnPlayerDie(BasePlayer player, HitInfo info)
        {
            if (!game.HasPlayer(player.name))
                return null;
            if (game.IsAfterDeathHandleOutOfLives(player))
            {
                timer.In(3f, () => PreHandleDeath(player));
                return null;
            }
            DropBodyOfPlayer(player);
            game.ResetPlayerToSpawn(player);
            return true;
        }

        private void DropBodyOfPlayer(BasePlayer player)
        {
            BaseCorpse corpse = player.DropCorpse("assets/prefabs/player/player_corpse.prefab");
            if (corpse != null)
            {

                LootableCorpse lootable = corpse as LootableCorpse;
                if (lootable != null)
                {
                    ItemContainer[] source = new ItemContainer[] { player.inventory?.containerMain, player.inventory?.containerWear, player.inventory?.containerBelt };
                    lootable.TakeFrom(source);
                    lootable.playerName = player.displayName;
                    lootable.playerSteamID = player.userID;
                }
                corpse.SpawnAsMapEntity();
            }
        }

        private void PreHandleDeath(BasePlayer player)
        {
            player.Respawn();
            HandleDeath(player);
        }

        private void HandleDeath(BasePlayer player)
        {
            if (player.IsSleeping() || player.IsDead() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.In(1f, () => HandleDeath(player));
                return;
            }
            TryToRemovePlayer(player);
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            // Not tested if this works
            player.Respawn();
            TryToRemovePlayer(player, true);
        }

        private void TryToRemovePlayer(BasePlayer player, bool teleport = false)
        {
            if (player == null)
                return;
            if (game.HasPlayer(player.name))
            {
                player.inventory.Strip();
                game.RemovePlayer(player, teleport);
            }
        }
        #endregion

        #region Main
        [ChatCommand("pvp")]
        void JoinPvPComand(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                if (game.HasPlayer(player.name))
                {
                    TryToRemovePlayer(player, true);
                    SendReply(player, "You have been removed from the pvp game.");
                    return;
                }
                SendReply(player, "Command usage: /pvp [1, 2]    " + 
                    "Joins a minigame arena on team 1 or 2. Enter this " +
                    "command again with no arguments to leave.");
                return;
            }
            int team = -1;
            int.TryParse(args[0], out team);
            if (team == 1 || team == 2) {
                if (player.IsSleeping() || player.IsDead() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
                {
                    SendReply(player, "You are sleeping/dead/receiving data so cannot join the game. Try again in a few seconds.");
                    return;
                }
                SendReply(player, "You have joined team " + team + ". To leave type: /pvp");
                game.AddPlayer(player, team);
            } else {
                SendReply(player, "Invalid team, please join team 1 or 2, i.e: /pvp 1");
            }
        }
        #endregion

        #region Utility
        [ChatCommand("gps")]
        void GetGpsCommand(BasePlayer player, string command, string[] args)
        {
            if (!HasAccess(player))
                return;
            var position = player.GetEstimatedWorldPosition();
            SendReply(player, position.x + ", " + position.y + ", " + position.z);
        }

        [ChatCommand("home")]
        void GoHomeCommand(BasePlayer player, string command, string[] args)
        {
            if (!HasAccess(player))
                return;
            player.Teleport(new Vector3(941.3005f, 11.07579f, -257.8884f));
        }

        bool HasAccess(BasePlayer player)
        {
            if (player.net.connection.authLevel < 1)
            {
                SendReply(player, "You don't have access to this command");
                return false;
            }
            return true;
        }
        #endregion

        #region Game
        class Game
        {
            public List<Vector3> TeamPositions { get; set; } = new List<Vector3>();
            private Dictionary<string, GamePlayer> players = new Dictionary<string, GamePlayer>();
            public uint StartingLives { get; set; } = 3;

            public Game()
            {
                TeamPositions.Add(new Vector3(1012.765f, 21.7402f, -350.6301f)); // Team 1
                TeamPositions.Add(new Vector3(997.1364f, 21.74019f, -365.8211f)); // Team 2
            }

            public bool IsAfterDeathHandleOutOfLives(BasePlayer player)
            {
                var gamePlayer = players[player.name];
                var lives = gamePlayer.DeductLives();
                if (lives == 0)
                {
                    player.SendConsoleCommand("chat.add", 0, "Your dead! Good luck next time.", 1f);
                    return true;
                }
                player.SendConsoleCommand("chat.add", 0, "You have " + lives + " lives remaining!", 1f);
                return false;
            }

            public void AddPlayer(BasePlayer player, int team)
            {
                if (team < 1 || team > TeamPositions.Count)
                    return;
                players.Add(player.name, new GamePlayer(team, player, StartingLives));
                ResetPlayerToSpawn(player);
            }

            public void ResetPlayerToSpawn(BasePlayer player)
            {
                if (!players.ContainsKey(player.name))
                    return;
                player.inventory.Strip();
                player.Teleport(TeamPositions[players[player.name].Team - 1]);
                Item item = Inventory.BuildWeapon(-853695669, 0, null); // Bow
                if (item != null)
                    player.GiveItem(item);
                item = Inventory.BuildItem(-420273765, 20, 0); // Arrows
                if (item != null)
                    player.GiveItem(item);
                item = Inventory.BuildItem(776005741, 1, 0); // Bone knife
                if (item != null)
                    player.GiveItem(item);
                player.health = 100f;
                player.metabolism.calories.value = 500f;
                player.metabolism.hydration.value = 500f;
            }

            public void RemovePlayer(BasePlayer player, bool teleport)
            {
                if (!players.ContainsKey(player.name))
                    return;
                GamePlayer gamePlayer = players[player.name];
                players.Remove(player.name);
                gamePlayer.Items.RestoreInventory(player);
                PlayerStatus status = gamePlayer.Status;
                player.health = status.Health;
                if (status.Hunger != null)
                {
                    player.metabolism.calories.value = (float) status.Hunger;
                }
                if (status.Thirst != null)
                {
                    player.metabolism.hydration.value = (float)status.Hunger;
                }
                if (teleport)
                    player.Teleport(gamePlayer.HomeLocation);
            }

            public bool HasPlayer(string name)
            {
                return players.ContainsKey(name);
            }
        }
        #endregion

        #region GamePlayer
        class GamePlayer
        {
            public int Team { get; set; }
            public Vector3 HomeLocation { get; set; }
            public Inventory Items { get; set; }
            public PlayerStatus Status { get; set; }
            public uint Lives { get; set; } = 3;

            public GamePlayer(int team, BasePlayer player, uint startingLives)
            {
                Team = team;
                HomeLocation = player.GetEstimatedWorldPosition();
                Items = new Inventory(player);
                Status = new PlayerStatus(player);
                Lives = startingLives;
            }

            public uint DeductLives()
            {
                Lives -= 1;
                return Lives;
            }
        }
        #endregion

        #region PlayerStatus
        class PlayerStatus
        {
            public float Health { get; set; }
            public float? Hunger { get; set; }
            public float? Thirst { get; set; }

            public PlayerStatus(BasePlayer player)
            {
                Health = player.health;
                Hunger = player.metabolism?.calories?.value;
                Thirst = player.metabolism?.hydration?.value;
            }
        }
        #endregion

        #region Inventory
        class Inventory
        {
            public List<SavedItem> BeltItems { get; set; } = new List<SavedItem>();
            public List<SavedItem> MainItems { get; set; } = new List<SavedItem>();
            public List<SavedItem> WearItems { get; set; } = new List<SavedItem>();

            public Inventory(BasePlayer player)
            {
                var playerInv = player?.inventory?.containerBelt?.itemList;
                if (playerInv != null)
                    foreach (Item item in playerInv)
                    {
                        if (item == null)
                            continue;
                        BeltItems.Add(new SavedItem(item));
                    }
                playerInv = player?.inventory?.containerMain?.itemList;
                if (playerInv != null)
                    foreach (Item item in playerInv)
                    {
                        if (item == null)
                            continue;
                        MainItems.Add(new SavedItem(item));
                    }
                playerInv = player?.inventory?.containerWear?.itemList;
                if (playerInv != null)
                    foreach (Item item in playerInv)
                    {
                        if (item == null)
                            continue;
                        WearItems.Add(new SavedItem(item));
                    }
            }

            public void RestoreInventory(BasePlayer player)
            {
                player.inventory.Strip();

                GivePlayerInventory(player, MainItems, 0);
                GivePlayerInventory(player, WearItems, 1);
                GivePlayerInventory(player, BeltItems, 2);
            }

            private void GivePlayerInventory(BasePlayer player, List<SavedItem> items, int container)
            {
                foreach (SavedItem item in items)
                {
                    if (item.Weapon)
                        item.GiveItem(player, BuildWeapon(item.ItemId, item.SkinId, null), container); // FIXME: Mods is parsed as null
                    else
                        item.GiveItem(player, BuildItem(item.ItemId, item.Amount, item.SkinId), container);
                }
            }

            public static Item BuildItem(int itemid, int amount, ulong skin)
            {
                if (amount < 1)
                    amount = 1;
                return ItemManager.CreateByItemID(itemid, amount, skin);
            }

            public static Item BuildWeapon(int id, ulong skin, List<int> mods)
            {
                Item item = ItemManager.CreateByItemID(id, 1, skin);
                var weapon = item.GetHeldEntity() as BaseProjectile;
                if (weapon != null)
                {
                    (item.GetHeldEntity() as BaseProjectile).primaryMagazine.contents = (item.GetHeldEntity() as BaseProjectile).primaryMagazine.capacity;
                }
                if (mods != null)
                {
                    foreach (var mod in mods)
                    {
                        item.contents.AddItem(BuildItem(mod, 1, 0).info, 1);
                    }
                }
                return item;
            }

            #region SavedItem
            public class SavedItem
            {
                public string ShortName { get; set; }
                public int ItemId { get; set; }
                public float Condition { get; set; }
                public int Amount { get; set; }
                public int AmmoAmount { get; set; }
                public string AmmoType { get; set; }
                public ulong SkinId { get; set; }
                public bool Weapon { get; set; }
                public List<SavedItem> mods;

                public SavedItem(Item item)
                {
                    ShortName = item.info?.shortname;
                    Amount = item.amount;
                    mods = new List<SavedItem>();
                    SkinId = item.skin;
                    ItemId = item.info.itemid;
                    Weapon = false;
                    if (item.hasCondition)
                        Condition = item.condition;
                    if (item.info.category.ToString() == "Weapon")
                    {
                        BaseProjectile weapon = item.GetHeldEntity() as BaseProjectile;
                        if (weapon != null)
                        {
                            if (weapon.primaryMagazine != null)
                            {
                                AmmoAmount = weapon.primaryMagazine.contents;
                                AmmoType = weapon.primaryMagazine.ammoType.shortname;
                                this.Weapon = true;
                                if (item.contents != null)
                                    foreach (var mod in item.contents.itemList)
                                        if (mod.info.itemid != 0)
                                            mods.Add(new SavedItem(mod));
                            }
                        }
                    }
                }

                public void GiveItem(BasePlayer player, Item item, int container)
                {
                    if (item == null) return;
                    ItemContainer cont;
                    switch (container)
                    {
                        case 1:
                            cont = player.inventory.containerWear;
                            break;
                        case 2:
                            cont = player.inventory.containerBelt;
                            break;
                        default:
                            cont = player.inventory.containerMain;
                            break;
                    }
                    player.inventory.GiveItem(item, cont);
                }

                public Item BuildItem(SavedItem sItem)
                {
                    if (sItem.Amount < 1) sItem.Amount = 1;
                    Item item = ItemManager.CreateByItemID(sItem.ItemId, sItem.Amount, sItem.SkinId);
                    if (item.hasCondition)
                        item.condition = sItem.Condition;
                    return item;
                }

                public Item BuildWeapon(SavedItem sItem)
                {
                    Item item = ItemManager.CreateByItemID(sItem.ItemId, 1, sItem.SkinId);
                    if (item.hasCondition)
                        item.condition = sItem.Condition;
                    var weapon = item.GetHeldEntity() as BaseProjectile;
                    if (weapon != null)
                    {
                        var def = ItemManager.FindItemDefinition(sItem.AmmoType);
                        weapon.primaryMagazine.ammoType = def;
                        weapon.primaryMagazine.contents = sItem.AmmoAmount;
                    }
                    if (sItem.mods != null)
                        foreach (var mod in sItem.mods)
                            item.contents.AddItem(BuildItem(mod).info, 1);
                    return item;
                }
            }
            #endregion
        }
        #endregion
    }
}
