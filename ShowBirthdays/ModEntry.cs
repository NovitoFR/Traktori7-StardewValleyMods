using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using static StardewValley.Menus.Billboard;


namespace ShowBirthdays
{
	class ModEntry : Mod
	{
        private Texture2D billboardTexture;

        private CycleType cycleType;
		// Flag for if the calendar is open
		private bool calendarOpen = false;

		// Current cycle in render ticks for changing the birthday sprite
		private int currentCycle = 0;

		private int clickedDay = -1;
		private Vector2 cursorPos = new Vector2();

		private BirthdayHelper bdHelper = null!;
		private ModConfig config = null!;

		private Texture2D? iconTexture;
		private readonly string assetName = PathUtilities.NormalizeAssetName("Traktori.ShowBirthdays/Icon");
		private readonly string iconPath = Path.Combine("assets", "Icon.png");


		public override void Entry(IModHelper helper)
		{
            // Initialize the helper
            bdHelper = new BirthdayHelper(Monitor, Helper.ModRegistry, Helper.GameContent);

            helper.Events.Content.AssetRequested += OnAssetRequested;
			helper.Events.Display.MenuChanged += OnMenuChanged;
			helper.Events.Display.RenderingActiveMenu += OnRenderingActiveMenu;
			helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
			helper.Events.GameLoop.GameLaunched += OnGameLaunched;
			helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
			helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
		}


		private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
		{
			if (e.Name.IsEquivalentTo(assetName))
			{
				e.LoadFromModFile<Texture2D>(iconPath, AssetLoadPriority.Low);
			}
		}


		private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
		{
			// Load the config for generic mod config menu
			config = Helper.ReadConfig<ModConfig>();
			var api = Helper.ModRegistry.GetApi<IGenericModConfigAPI>("spacechase0.GenericModConfigMenu");

			// Register options for GMCM
			if (api is not null)
			{
				api.Register(
					mod: ModManifest,
					reset: () => config = new ModConfig(),
					save: () => Helper.WriteConfig(config)
				);

				api.AddNumberOption(
					mod: ModManifest,
					getValue: () => config.cycleDuration,
					setValue: (int val) => config.cycleDuration = val,
					name: () => Helper.Translation.Get("duration-label"),
					tooltip: () => Helper.Translation.Get("duration-desc"),
					min: 1,
					max: 600
				);

				api.AddTextOption(
					mod: ModManifest,
					getValue: () => config.cycleType,
					setValue: (string val) => ChangeCycleType(val),
					name: () => Helper.Translation.Get("type-label"),
					tooltip: () => Helper.Translation.Get("type-desc"),
					allowedValues: ModConfig.cycleTypes
				);

				api.AddBoolOption(
					mod: ModManifest,
					getValue: () => config.showIcon,
					setValue: (bool val) => config.showIcon = val,
					name: () => Helper.Translation.Get("icon-label"),
					tooltip: () => Helper.Translation.Get("icon-desc")
				);
			}
		}


		private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
		{
			// Unsubscribe from the events to reduce unnecessary event checks
			Helper.Events.Input.ButtonPressed -= OnButtonPressed;
			Helper.Events.Input.CursorMoved -= OnCursorMoved;
		}


		private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
		{
			// Load the icon from the mod folder
			iconTexture = Helper.GameContent.Load<Texture2D>(assetName);
            billboardTexture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\Billboard");

            // Check if the loading succeeded
            if (iconTexture is null)
			{
				Monitor.Log("Failed loading the icon " + assetName, LogLevel.Error);
			}

            // Check if the loading succeeded
            if (billboardTexture is null)
            {
                Monitor.Log("Failed loading the billboard texture", LogLevel.Error);
            }

            // Refresh the config
            config = Helper.ReadConfig<ModConfig>();

			// Update the cycle type
			ChangeCycleType(config.cycleType);
		}


		/// <summary>
		/// Edits the calendar's hover texts to include extra birthdays.
		/// </summary>
		private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
		{
			if (!IsCalendarActive(e.NewMenu))
			{
                return;
			}

			bdHelper.RecheckBirthdays();

			// List of all the birthday days for the season
			List<int> list = bdHelper.GetDays(Game1.season);

			// Get the calendar's days since the menu is quaranteed to be the calendar
			Billboard billboard = (e.NewMenu as Billboard)!;
			List<ClickableTextureComponent> days = billboard.calendarDays;

			// NOTE: Remember that i goes from 1 to 28, so substract 1 from it to use as the index!
			for (int i = 1; i <= days.Count; i++)
			{
				// Add the NPC textures
				if (list.Contains(i))
				{
					days[i - 1].texture = bdHelper.GetSprite(Game1.season, i, false);
				}
			}
		}


		/// <summary>
		/// Changes the NPC sprites according to the selected cycle type
		/// </summary>
		private void OnRenderingActiveMenu(object? sender, RenderingActiveMenuEventArgs e)
		{
			if (!IsCalendarActive(Game1.activeClickableMenu))
				return;

			if (currentCycle < config.cycleDuration)
				currentCycle++;

			// Get birthday days that are shared
			List<int> listOfDays = bdHelper.GetDays(Game1.season, true);

			if (listOfDays.Count == 0)
				return;

			// Get the calendar's days since the menu is quaranteed to be the calendar
			Billboard billboard = (Game1.activeClickableMenu as Billboard)!;
			List<ClickableTextureComponent> days = billboard.calendarDays;

            switch (cycleType)
			{
				case CycleType.Always:
					if (currentCycle >= config.cycleDuration)
					{
						for (int i = 0; i < listOfDays.Count; i++)
						{
							try
							{
								days[listOfDays[i] - 1].texture = bdHelper.GetSprite(Game1.season, listOfDays[i], true);
                            }
							catch (Exception ex)
							{
								Monitor.Log("There was a problem with parsing the birthday data", LogLevel.Error);
								Monitor.Log(ex.ToString(), LogLevel.Error);
							}
						}

						// Reset the cycle
						currentCycle = 0;
					}
					break;
				case CycleType.Hover:
					if (currentCycle >= config.cycleDuration)
					{
						for (int i = 0; i < listOfDays.Count; i++)
						{
							if (days[listOfDays[i] - 1].containsPoint((int)cursorPos.X, (int)cursorPos.Y))
							{
								days[listOfDays[i] - 1].texture = bdHelper.GetSprite(Game1.season, listOfDays[i], true);
							}
						}

						// Reset the cycle
						currentCycle = 0;
					}
					break;
				case CycleType.Click:
					if (clickedDay != -1 && listOfDays.Contains(clickedDay))
					{
						days[clickedDay - 1].texture = bdHelper.GetSprite(Game1.season, clickedDay, true);
						clickedDay = -1;
					}
					break;
				default:
					Monitor.Log($"Unknown cycle type {cycleType} encountered in OnRenderingActiveMenu", LogLevel.Error);
					break;
			}
		}


		/// <summary>
		/// Draws the icon for shared birthdays and redraws the cursor and hover text
		/// TODO: Figure out a way to do this without redrawing the cursor and the hover text?
		/// </summary>
		private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
		{
			if (!config.showIcon || !IsCalendarActive(Game1.activeClickableMenu) || iconTexture is null)
				return;

			// Get birthday days that are shared
			List<int> listOfDays = bdHelper.GetDays(Game1.season , true);

			if (listOfDays.Count == 0)
				return;

			// Get the calendar's days since the menu is quaranteed to be the calendar
			Billboard billboard = (Game1.activeClickableMenu as Billboard)!;
			List<ClickableTextureComponent> days = billboard.calendarDays;

			int offsetX = iconTexture.Width * Game1.pixelZoom;
			int offsetY = iconTexture.Height * Game1.pixelZoom;

			for (int i = 0; i < listOfDays.Count; i++)
			{
                ClickableTextureComponent clickableTextureComponent = days[listOfDays[i] - 1];

				// Redraw the day background
                e.SpriteBatch.Draw(Game1.fadeToBlackRect, clickableTextureComponent.bounds, Color.Black);
                e.SpriteBatch.Draw(billboardTexture, clickableTextureComponent.bounds, new Rectangle(38 + (listOfDays[i] - 1) % 7 * 32, 248 + (listOfDays[i] - 1) / 7 * 32, 31, 31), Color.White);

				// Redraw the day content
                if (billboard.calendarDayData.TryGetValue(clickableTextureComponent.myID, out var value))
                {
                    // Subsitutes the texture from calendarDayData with the one we stored in days
                    if (days[listOfDays[i] - 1].texture != null && value.Texture != null)
                    {
                        e.SpriteBatch.Draw(days[listOfDays[i] - 1].texture, new Vector2(clickableTextureComponent.bounds.X + 48, clickableTextureComponent.bounds.Y + 28), value.TextureSourceRect, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
                    }

					// From here on down to the icon texture drawing, same as default behavior
                    if (value.Type.HasFlag(BillboardEventType.PassiveFestival))
                    {
                        Utility.drawWithShadow(e.SpriteBatch, Game1.mouseCursors, new Vector2(clickableTextureComponent.bounds.X + 12, (float)(clickableTextureComponent.bounds.Y + 60) - Game1.dialogueButtonScale / 2f), new Rectangle(346, 392, 8, 8), value.GetEventOfType(BillboardEventType.PassiveFestival).locked ? (Color.Black * 0.3f) : Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                    }

                    if (value.Type.HasFlag(BillboardEventType.Festival))
                    {
                        Utility.drawWithShadow(e.SpriteBatch, billboardTexture, new Vector2(clickableTextureComponent.bounds.X + 40, (float)(clickableTextureComponent.bounds.Y + 56) - Game1.dialogueButtonScale / 2f), new Rectangle(1 + (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 600.0 / 100.0) * 14, 398, 14, 12), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                    }

                    if (value.Type.HasFlag(BillboardEventType.FishingDerby))
                    {
                        Utility.drawWithShadow(e.SpriteBatch, Game1.mouseCursors_1_6, new Vector2(days[listOfDays[i] - 1].bounds.X + 8, (float)(days[listOfDays[i] - 1].bounds.Y + 60) - Game1.dialogueButtonScale / 2f), new Rectangle(103, 2, 10, 11), Color.White, 0f, Vector2.Zero, 4f, flipped: false, 1f);
                    }

                    if (value.Type.HasFlag(BillboardEventType.Wedding))
                    {
                        e.SpriteBatch.Draw(Game1.mouseCursors2, new Vector2(clickableTextureComponent.bounds.Right - 56, clickableTextureComponent.bounds.Top - 12), new Rectangle(112, 32, 16, 14), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
                    }

                    if (value.Type.HasFlag(BillboardEventType.Bookseller))
                    {
                        e.SpriteBatch.Draw(Game1.mouseCursors_1_6, new Vector2((float)(clickableTextureComponent.bounds.Right - 72) - 2f * (float)Math.Sin((Game1.currentGameTime.TotalGameTime.TotalSeconds + (double)(listOfDays[i] - 1) * 0.3) * 3.0), (float)(clickableTextureComponent.bounds.Top + 52) - 2f * (float)Math.Cos((Game1.currentGameTime.TotalGameTime.TotalSeconds + (double)(listOfDays[i] - 1) * 0.3) * 2.0)), new Rectangle(71, 63, 8, 15), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
                    }
                }

                if (Game1.dayOfMonth > (listOfDays[i] - 1) + 1)
                {
                    e.SpriteBatch.Draw(Game1.staminaRect, clickableTextureComponent.bounds, Color.Gray * 0.25f);
                }
                else if (Game1.dayOfMonth == (listOfDays[i] - 1) + 1)
                {
                    int num = (int)(4f * Game1.dialogueButtonScale / 8f);
                    IClickableMenu.drawTextureBox(e.SpriteBatch, Game1.mouseCursors, new Rectangle(379, 357, 3, 3), clickableTextureComponent.bounds.X - num, clickableTextureComponent.bounds.Y - num, clickableTextureComponent.bounds.Width + num * 2, clickableTextureComponent.bounds.Height + num * 2, Color.Blue, 4f, drawShadow: false);
                }

                // Add the multiple birthdays icon texture to the bottom right of the day
                Vector2 position = new Vector2(days[listOfDays[i] - 1].bounds.Right - offsetX, days[listOfDays[i] - 1].bounds.Bottom - offsetY);
                e.SpriteBatch.Draw(iconTexture, new Rectangle((int)position.X, (int)position.Y, offsetX, offsetY), Color.White);
            }
            
            // Redraw the cursor
            billboard.drawMouse(e.SpriteBatch);

			// The current hover text is stored in the billboard itself
			string text = Helper.Reflection.GetField<string>(billboard, "hoverText").GetValue();
			
			// Redraw the hover text
			if (text.Length > 0)
			{
				IClickableMenu.drawHoverText(e.SpriteBatch, text, Game1.dialogueFont);
			}
		}


		/// <summary>
		/// Handles changing the sprite if the day as clicked
		/// </summary>
		private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
		{
			if (!calendarOpen || cycleType != CycleType.Click)
				return;

			if (e.Button == SButton.MouseLeft)
			{
				// Dangerous conversion, but calendarOpen should prevent problems for now
				List<ClickableTextureComponent> days = (Game1.activeClickableMenu as Billboard)!.calendarDays;

				if (days is null || days.Count < 28)
				{
					Monitor.Log("Calendar days are messed up for some reason in OnButtonPressed, aborting any drawing by the mod.");
					return;
				}

				for (int i = 0; i < days.Count; i++)
				{
					// Force the game in UIMode to get the correct scaling for pixel coordinates, since for calendar UIMode = false, for some reason...
					// TODO: Test if this is still the case
					Game1.InUIMode(() =>
					{
						Vector2 point = e.Cursor.GetScaledScreenPixels();

						if (days[i].containsPoint((int)point.X, (int)point.Y))
						{
							clickedDay = i + 1;
							Monitor.Log($"Player clicked on day {clickedDay} at {point}");
						}
					});
				}
			}
		}


		/// <summary>
		/// Tracks the cursor position to change the sprite if hovered over
		/// TODO: Broken for 1.5.6 for some reason. Did the ui scaling get fixed/changed or something?
		/// </summary>
		private void OnCursorMoved(object? sender, CursorMovedEventArgs e)
		{
			if (cycleType != CycleType.Hover || !calendarOpen)
				return;
			
			// Force the game in UIMode to get the correct scaling for pixel coordinates, since for calendar UIMode = false, for some reason...
			// TODO: Test if this is still the case
			Game1.InUIMode(() =>
			{
				cursorPos = e.NewPosition.GetScaledScreenPixels();
			});
		}


		/// <summary>
		/// Checks if the birthday calendar is active and updates the flag for it
		/// </summary>
		private bool IsCalendarActive([NotNullWhen(true)] IClickableMenu? activeMenu)
		{
			// Set to false by default to avoid repeating and switch to true at the end if everything was okay
			calendarOpen = false;

			if (activeMenu is null || activeMenu is not Billboard billboard)
				return false;

			// Check if we're looking the calendar or the quest part of the billboard
			bool dailyQuests = Helper.Reflection.GetField<bool>(billboard, "dailyQuestBoard").GetValue();

			// Do nothing if looking at the quest board
			if (dailyQuests)
				return false;

			List<ClickableTextureComponent> days = billboard.calendarDays;

			if (days is null || days.Count< 28)
			{
				Monitor.Log("Calendar days are messed up for some reason in OnMenuChanged, aborting any drawing by the mod.");
				return false;
			}

			// We are looking at the calendar and everything seems to be ok so update the flag for it
			calendarOpen = true;

			return true;
		}


		/// <summary>
		/// Updates the cycle type and the needed input events
		/// </summary>
		private void ChangeCycleType(string newType)
		{
			// Unsubscribe from the events just incase
			Helper.Events.Input.ButtonPressed -= OnButtonPressed;
			Helper.Events.Input.CursorMoved -= OnCursorMoved;

			//Change the cycle type and subscribe to the correct events to prevent unnecessary event checks
			config.cycleType = newType;

			switch (newType)
			{
				case "Always":
					cycleType = CycleType.Always;
					break;
				case "Hover":
					cycleType = CycleType.Hover;
					Helper.Events.Input.CursorMoved += OnCursorMoved;
					break;
				case "Click":
					cycleType = CycleType.Click;
					Helper.Events.Input.ButtonPressed += OnButtonPressed;
					break;
				default:
					Monitor.Log("The only accepted cycle types are Always, Hover and Click. Defaulting to Always.", LogLevel.Error);
					cycleType = CycleType.Always;
					break;
			}
		}

		enum CycleType
		{
			Always,
			Hover,
			Click
		}
	}
}
