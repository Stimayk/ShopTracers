using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;
using System.Drawing;

namespace ShopTracers
{
    public class ShopTracers : BasePlugin
    {
        public override string ModuleName => "[SHOP] Tracers";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.2";

        private IShopApi? SHOP_API;
        private const string CategoryName = "Tracers";
        public static JObject? JsonTracers { get; private set; }
        private readonly PlayerTracers[] playerTracers = new PlayerTracers[65];
        private static readonly Vector VectorZero = new(0, 0, 0);
        private static readonly QAngle RotationZero = new(0, 0, 0);

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/Tracers.json");
            if (File.Exists(configPath))
            {
                JsonTracers = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonTracers == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Cледы от пуль");

            var sortedItems = JsonTracers.Properties()
                .Where(p => p.Value is JObject)
                .Select(p => new { Key = p.Name, Value = (JObject)p.Value })
                .ToList();

            int teamIndex = sortedItems.FindIndex(p => p.Key == "Team");
            if (teamIndex != -1)
            {
                var teamItem = sortedItems[teamIndex];
                sortedItems.RemoveAt(teamIndex);
                sortedItems.Insert(0, teamItem);
            }

            int randomIndex = sortedItems.FindIndex(p => p.Key == "Random");
            if (randomIndex != -1)
            {
                var randomItem = sortedItems[randomIndex];
                sortedItems.RemoveAt(randomIndex);

                int insertIndex = teamIndex != -1 ? 1 : 0;
                sortedItems.Insert(insertIndex, randomItem);
            }

            foreach (var item in sortedItems)
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(
                        item.Key,
                        (string)item.Value["name"]!,
                        CategoryName,
                        (int)item.Value["price"]!,
                        (int)item.Value["sellprice"]!,
                        (int)item.Value["duration"]!
                    );
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot => playerTracers[playerSlot] = null!);
        }

        public HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName, int buyPrice, int sellPrice, int duration, int count)
        {
            string color;
            if (uniqueName == "Team")
            {
                color = "team";
            }
            else if (uniqueName == "Random")
            {
                color = "random";
            }
            else if (!TryGetItemColor(uniqueName, out color))
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'color' in config!");
                return HookResult.Continue;
            }

            playerTracers[player.Slot] = new PlayerTracers(color, itemId);
            return HookResult.Continue;
        }

        public HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1)
            {
                string color;
                if (uniqueName == "Team")
                {
                    color = "team";
                }
                else if (uniqueName == "Random")
                {
                    color = "random";
                }
                else if (!TryGetItemColor(uniqueName, out color))
                {
                    Logger.LogError($"{uniqueName} has invalid or missing 'color' in config!");
                    return HookResult.Continue;
                }

                playerTracers[player.Slot] = new PlayerTracers(color, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
            return HookResult.Continue;
        }

        public HookResult OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerTracers[player.Slot] = null!;
            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Pre)]
        public HookResult BulletImpact(EventBulletImpact @event, GameEventInfo info)
        {
            if (@event.Userid != null)
            {
                CCSPlayerController player = @event.Userid;
                if (playerTracers[player.Slot] != null && player.Pawn.Value?.AbsOrigin != null)
                {
                    if (JsonTracers!["Hide_Opposite_Team"]?.Value<int>() == 1 && player.TeamNum != GetClientTeam(@event.Userid))
                    {
                        return HookResult.Continue;
                    }

                    Vector PlayerPosition = player.Pawn.Value.AbsOrigin;
                    Vector BulletOrigin = new(PlayerPosition.X, PlayerPosition.Y, PlayerPosition.Z + 57);
                    Vector bulletDestination = new(@event.X, @event.Y, @event.Z);

                    Color tracerColor = GetTracerColor(player);

                    DrawLaserBetween(BulletOrigin, bulletDestination, tracerColor, (float)JsonTracers["life"]!, (float)JsonTracers["StartWidth"]!, (float)JsonTracers["EndEidth"]!);
                }
            }
            return HookResult.Continue;
        }

        private static int GetClientTeam(CCSPlayerController player)
        {
            if (player == null)
            {
                return 0;
            }
            return (int)player.TeamNum;
        }

        private Color GetTracerColor(CCSPlayerController player)
        {
            string colorString = playerTracers[player.Slot].Color;

            if (colorString == "random")
            {
                return GetRandomColor();
            }
            else if (colorString == "team")
            {
                return player.TeamNum == 3 ? Color.Blue : Color.Yellow;
            }
            else
            {
                try
                {
                    return ColorTranslator.FromHtml(colorString);
                }
                catch (Exception)
                {
                    return Color.White;
                }
            }
        }

        private static Color GetRandomColor()
        {
            Random rnd = new();
            return Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256));
        }

        public (int, CBeam?) DrawLaserBetween(Vector startPos, Vector endPos, Color color, float life, float startWidth, float endWidth)
        {
            if (startPos == null || endPos == null)
            {
                return (-1, null);
            }

            CBeam? beam = Utilities.CreateEntityByName<CBeam>("beam");

            if (beam == null)
            {
                return (-1, null);
            }

            beam.Render = color;
            beam.Width = startWidth / 2.0f;
            beam.EndWidth = endWidth / 2.0f;

            beam.Teleport(startPos, RotationZero, VectorZero);
            beam.EndPos.X = endPos.X;
            beam.EndPos.Y = endPos.Y;
            beam.EndPos.Z = endPos.Z;
            beam.DispatchSpawn();

            AddTimer(life, () =>
            {
                try
                {
                    if (beam.IsValid)
                    {
                        beam.Remove();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to remove beam entity: {ex.Message}");
                }
            });

            return ((int)beam.Index, beam);
        }

        private static bool TryGetItemColor(string uniqueName, out string color)
        {
            color = "";
            if (JsonTracers != null && JsonTracers.TryGetValue(uniqueName, out JToken? obj) && obj is JObject jsonItem && jsonItem["color"] != null && jsonItem["color"]!.Type != JTokenType.Null)
            {
                color = jsonItem["color"]!.ToString();
                return true;
            }
            return false;
        }

        public record PlayerTracers(string Color, int ItemID);
    }
}