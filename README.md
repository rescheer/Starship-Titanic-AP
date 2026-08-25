# Starship Titanic - Archipelago Mod
A Starship Titanic .apworld and client for use with [Archipelago](https://archipelago.gg/). Both are in an **early alpha state**, and should not currently be used in live games.

**For use with the Steam version of Starship Titanic** 

Requires [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## Installation
1. Download the [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) if you do not already have it
2. Extract the client anywhere on your PC
3. Double-click the .apworld to add it to your Archipelago custom worlds
4. In the Archipelago launcher, run the "Generate Template Options" tool to generate a template .yaml file in your Player/Templates folder
5. Edit the .yaml raw or with your preferred editor
6. Provide the .yaml file to the host of the room, or visit [archipelago.gg](https://archipelago.gg/tutorial/) for details on generating your own multiworld
7. Start the game and the client. Click Attach in the client and fill out the server info. 
8. Click connect and play

## Playing the AP
- Receiving items: When you are sent an item from the multiworld, **it does not immediately end up in your inventory.** You need to go to any Succ-U-Bus and press the Receive button in your PET's Control tab (middle icon). The Succ-U-Bus will dispense your item into the tray. Don't forget to pick it up!
   - If you have multiple items queued for delivery, simply press Receive again. Don't forget to take your item out of the tray after each arrives
- All Archipelago server messages including location checks and granted items will be routed to your PET on the Conversation tab
- Any message starting with an exclamation point (!) in the Conversation text box will be sent to the Archipelago server. Examples:
  - ```!Hey everyone``` Send a chat message
  - ```!!hint``` Sends ```!hint``` to the Archipelago server, returning your current hint points.
  - ```!!help``` Sends ```!help``` to the Archipelago server, listing all the commands available

## Locations and Items
Locations in this apworld include:
- Picking up inventory items
- Solving puzzles
- Visiting rooms for the first time
- And more planned

Items include most carryable inventory items in the game as well as two Progressive Class Upgrades, which unlock more areas of the ship to explore.

## AI Disclosure
Generative AI was consulted and used in early development, especially to assist in searching through the game's memory to find a stable anchor to attach the client to, as well as memory offsets of items and functions. As this is a learning project for me, I'm writing more and more of my own code as the project progresses, as well as rewriting some of the machine-authored foundational code.