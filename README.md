# Starship Titanic - Archipelago Mod
A Starship Titanic client for use with [Archipelago](https://archipelago.gg/). Stability has come a long way, but this should still be used in live games with caution.

**For use with the Steam release of Starship Titanic only** 

Requires [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## Installation
*This guide assumes you already have Archipelago installed on your computer*
1. Download the [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) if you do not already have it
2. Extract the client anywhere on your PC
3. Download the .apworld and double-click it to add it to your Archipelago custom worlds
4. In the Archipelago launcher, run the "Generate Template Options" tool to generate a template .yaml file in your Player/Templates folder
5. Edit the .yaml with your preferred editor
6. Provide the .yaml file to the host of the room, or visit [archipelago.gg](https://archipelago.gg/tutorial/) for details on generating your own multiworld
7. Start the game and the client. Click Attach in the client (if needed, the client will auto-attach if started after the game), then click Connect to AP... and fill out the server info. 
8. Click connect and play

## Playing the AP
- Receiving items: By default (Item Delivery Method: Succubus), when you are sent an item from the multiworld, **it does not immediately end up in your inventory.** You need to go to any Succ-U-Bus and press the Receive button in your PET's Control tab (middle icon). The Succ-U-Bus will dispense your item into the tray. Don't forget to pick it up!
   - If you have multiple items queued for delivery, simply press Receive again. Don't forget to take your item out of the tray after each arrives
   - With Item Delivery Method set to Direct To Inventory, received items go straight into your inventory instead
- All Archipelago server messages including location checks and granted items will be routed to your PET on the Conversation tab
- Client commands - Usable in-game or in the client window:
  - **/help** - list all client commands
  - **/ap \<text\>** - sends \<text\> to the AP server from in game
  - **/hint** - alias for /ap !hint. Used the same way as AP's !hint command
  - **/force_seed** - Forces the client to accept the current save as the correct save for the connected slot
  - **/return \<item\>** - [experimental] Returns an item to your inventory. Only works with items already received from the multiworld. Recommended only as a bug recovery tool
  - **/getmail** - [experimental] Moves all the pending granted AP items from the mail system straight into your inventory. Skip listening to the Succ-U-Bus vomiting sounds! (after 170 hours of testing, I am overjoyed)
  - **/loc** - prints your current location data
  - **/clues** - shows the Parrot Hunt clues revealed so far
### Parrot Hunt goal: 
Oh no! The parrot has gotten himself stuck in a random 2nd or 1st Class Stateroom's Succ-U-Bus! Find Progressive Clues in the multiworld. Some of them reveal the stateroom's elevator, floor or room number. Once you know all three (or before, if you have a guess!), go to that stateroom's Succ-U-Bus terminal and save the parrot from its clutches!
 
## FAQ
- Why only the Steam version of the game? What about GOG? Can I use my original discs?
  - Simply put, the Steam version is the one that I own. I plan to eventually pick up the GOG version and test it, because there's a good chance it's the same ScummVM version of the game and might work out of the box. The original discs will never be supported. This implementation depends on the rebuilt engine used in the ScummVM release
- I was granted an item, but it's not in my inventory
  - When the Item Delivery Method YAML option is set to Succ-U-Bus (default), granted items are placed into the Succ-U-Bus mail delivery system. There is a Succ-U-Bus terminal in just about every room in the ship. Approach one and use the Receive button in the UI to receive an item. Repeat for any additional items
- The Deskbot said she upgraded my class and/or assigned me a stateroom, but nothing happened
  - Class upgrades and Stateroom assignments are now multiworld items. Completing the processes to get class upgrades from the Deskbot are still location checks, though
- What do I do next?
  - Despite the randomization, all of the puzzles in the game work the same as they do in a vanilla game. Walkthroughs are really useful for this game, especially if you've never played it

## Locations and Items
[Locations](https://github.com/rescheer/Starship-Titanic-AP/wiki/Locations) in this apworld include:
- Picking up inventory items
- Solving puzzles
- Visiting rooms for the first time
- Visiting every Succ-U-Bus terminal on the ship

[Items](https://github.com/rescheer/Starship-Titanic-AP/wiki/Items) include:
- Most carryable inventory items
- Progressive Class Upgrades (Optional, default on)
- Progressive Staterooms (Optional, default on)
- Access Cards for all ship areas that must be acquired from the multiworld (Optional, default off)

## Generative AI Use Disclosure
- Claude Code was used moderately in the development of this AP integration, especially for reverse engineering, finding memory offsets, and building hooks to manipulate game logic
- The majority of the apworld code was written by hand
- All generated code was reviewed, edited, and tested by hand
- The design and features of this integration were made without creative input from AI
