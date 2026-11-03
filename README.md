**<u>[Client](https://github.com/LuigiARC/Persona5AP.Client/)</u> [Apworld](https://github.com/LuigiARC/Archipelago-P5R/releases/) [Details](https://github.com/LuigiARC/Archipelago-P5R/blob/main/worlds/p5r/docs/en_Persona%205%20Royal.md) [Setup](https://github.com/LuigiARC/Archipelago-P5R/blob/main/worlds/p5r/docs/setup_en.md)**

# Persona 5 AP Client
    
Persona 5 Royal Archipelago client for Reloaded-II. This mod is *very much* in alpha.
Use a disposable save and keep a backup. 

The F9 connection, shop and warp menu should work after the casino intro.

The separate APWorld is maintained under `worlds/p5r` in the companion Archipelago
fork. Install its `p5r.apworld` release to generate/host games. 

Using Universal Tracker is highly recommended. You may need to send location thorough the cheat console. 

> I did a full playthrough of the mod with a few friends and there were some issues with Maruki's palace ending. 
> There is some flag that I have not found so the stairway to the final boss is not unlocked.
> If I recall correctly 95% of the other things worked fine in that playthrough. But do bear in mind this is an alpha, so crashes and bugs are to be expected and you might have to release eventually if there is a hard lock / game breaking bug.

## Install

1. Download the client from the latest release
2. Back up your saves and import `Persona 5 AP.zip` into Reloaded-II.
3. Enable the client and its dependencies for `p5r.exe`: Persona Essentials
   (`p5rpc.modloader`), CRI FileSystem V2 Hook (`crifs.v2.hook`), SharedLib Hooks
   and BF File Emulator. Enable the additional dependencies requested by Reloaded.
4. Launch through Reloaded-II and connect using F9 after the casino intro.

## Build

Requires the .NET 8 SDK and Python. From this repository root:

```bash
python tools/sync_catalogs.py --check
dotnet restore src/P5RArchipelago/P5RArchipelago.csproj
dotnet build src/P5RArchipelago/P5RArchipelago.csproj -c Release --no-restore
python tools/package_mod.py
```

Output: `dist/Persona 5 AP.zip`.

## License and credits

Original client code: **GPL-3.0-only**, Copyright (c) 2026 Luigi; see LICENSE.
Shared catalog contributions are MIT. See THIRD_PARTY_NOTICES.md for crediting.

All of the resources used in the development of this were individually looked for and found by me. This would not have been possible without these projects.

## AI Disclosure

AI was used in this project, but to say this is vibe-coded would be an insult to me. There is basically no precedent for mods like this for Atlus / Persona games. Some very vague resources are available online like some of the decompiled table names, but it's very bare. I have spent many hours over a span of months studying the game and testing to try and to get this to a playable state, which I'm still convinced it isn't fully. I'm confident I know the codebase well enough to release it and most of the stuff that I've tested works but it is undeniably in alpha and unstable state right now. Also I will add that I use self hosted models pretty much exclusively. Personally, I don't support the major AI companies and don't like their behaviour environmentally and ethically.

Also in regards to vibe-coding, I don't know how people say they do it with a straight face to be perfectly honest. The one time I tried to use a model to make something from scratch without actively monitoring / rewriting stuff / fighting with its crap decisions it resulted in this: https://streamable.com/mgehr0
