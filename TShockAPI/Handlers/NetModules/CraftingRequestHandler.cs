using System;
using System.IO;
using Terraria;
using Terraria.GameContent;
using Terraria.Net;

namespace TShockAPI.Handlers.NetModules
{
	/// <summary>
	/// Handles the CraftingRequests net module. Prevents players from crafting using nearby chests
	/// in regions where they do not have build permission, when <see cref="Configuration.TShockSettings.RegionProtectChests"/> is enabled.
	/// Checks each nearby chest's position against region permissions rather than only the player's position,
	/// so that non-owners cannot pull materials from chests inside protected regions even if the player
	/// is standing outside the region boundary.
	/// When a crafting request is denied, sends a denial response back to the client so it properly
	/// cleans up the FakeCursorItem and refunds any locally consumed materials.
	/// </summary>
	public class CraftingRequestHandler : INetModuleHandler
	{
		/// <summary>
		/// The approximate horizontal range (in tiles) that Terraria's crafting system uses
		/// when scanning for nearby chests to pull materials from.
		/// </summary>
		private const int CraftingChestRangeX = 37;

		/// <summary>
		/// The approximate vertical range (in tiles) that Terraria's crafting system uses
		/// when scanning for nearby chests to pull materials from.
		/// </summary>
		private const int CraftingChestRangeY = 40;

		/// <summary>
		/// Reads the crafting request data from the net module. Currently a no-op as we only need the player's position
		/// and nearby chest locations to perform region permission checks.
		/// </summary>
		/// <param name="data"></param>
		public void Deserialize(MemoryStream data)
		{
			// The crafting request packet contains recipe/item data that Terraria processes server-side.
			// We do not need to deserialize it for our region permission check.
		}

		/// <summary>
		/// Rejects crafting requests if any nearby chest (within crafting range) is inside a region
		/// where the player lacks build permission and <see cref="Configuration.TShockSettings.RegionProtectChests"/> is enabled.
		/// This ensures that crafting-from-nearby-chests only works when the player is the region owner
		/// (or is otherwise allowed to build in the region).
		/// When rejecting, sends a <c>WriteResponse(approved: false)</c> back to the client so it
		/// properly removes the FakeCursorItem and refunds consumed materials.
		/// </summary>
		/// <param name="player"></param>
		/// <param name="rejectPacket"></param>
		public void HandlePacket(TSPlayer player, out bool rejectPacket)
		{
			TShock.Log.ConsoleDebug(GetString(
				"CraftingRequestHandler / HandlePacket invoked for {0} at ({1},{2}), RegionProtectChests={3}",
				player.Name, player.TileX, player.TileY, TShock.Config.Settings.RegionProtectChests));

			if (TShock.Config.Settings.RegionProtectChests)
			{
				// Scan all world chests to find any within crafting range that the player
				// does not have permission to access. This covers the case where a player
				// is standing outside a protected region but chests inside it are within
				// the crafting range, as well as the case where the player is inside a
				// region they do not own.
				for (int i = 0; i < Main.maxChests; i++)
				{
					var chest = Main.chest[i];
					if (chest == null)
						continue;

					// Check if this chest is within crafting range of the player
					int dx = Math.Abs(chest.x - player.TileX);
					int dy = Math.Abs(chest.y - player.TileY);
					if (dx > CraftingChestRangeX || dy > CraftingChestRangeY)
						continue;

					// Check if the player has build permission at this chest's location
					if (!player.HasBuildPermission(chest.x, chest.y, false))
					{
						TShock.Log.ConsoleDebug(GetString(
							"CraftingRequestHandler / HandlePacket rejected crafting from nearby chests due to region protection from {0} (chest at {1},{2})",
							player.Name, chest.x, chest.y));
						player.SendErrorMessage(GetString("You do not have permission to craft from nearby chests in this region."));

						// Send a denial response back to the client so it properly cleans up.
						// Without this, the client stays stuck with a FakeCursorItem on the cursor
						// and consumed inventory items are never refunded, because the client is
						// waiting for a server response that the rejected packet prevents.
						NetManager.Instance.SendToClient(
							CraftingRequests.NetCraftingRequestsModule.WriteResponse(approved: false),
							player.Index);

						rejectPacket = true;
						return;
					}
				}
			}

			rejectPacket = false;
		}
	}
}

