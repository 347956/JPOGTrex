# JPOGTrex 🦖

This repository contains the full source code for the JPOGTrex for Lethal Company, including its Unity content (prefab, model, animations and Dusk definitions).  
The guide I followed for this project can be found [here](https://lethal.wiki/dev/apis/lethallib/custom-enemies/overview.).  
A video Tutorial can be found [here](https://www.youtube.com/watch?v=NZ_F8wDczzM).

 > [!NOTE]  
 > The wiki page might be slightly out of date regarding how the JPOGTrex prefab is constructed.

> [!NOTE]  
> I do not own any of the Assets nor made them myself. The models, animations and some of the audio clips were made by: [Blue Tongue Entertainment](https://en.wikipedia.org/wiki/Blue_Tongue_Entertainment), which was closed down in 2011.  
> I also got some sounds for the T-Rex from the game: [Primal Carnage: Extinction](https://store.steampowered.com/app/321360/Primal_Carnage_Extinction/).

## Repository layout 📁

This repository is a folder that goes inside the `Assets/` folder of a Lethal Company Unity project, for example `Assets/LethalCompany/Mods/plugins/JPOGTrex/`. It holds all of the mod's own Unity content and its code. The host Unity project supplies the generic parts: the game scripts (Assembly-CSharp), the Unity packages and DawnLib (`com.github.teamxiaolan.dawnlib.dusk.dll`).

| Path | Contents |
| --- | --- |
| `Content/JPOGTrex/` | The enemy: prefab, model, animations, materials, audio, bestiary entries and Dusk registry assets. |
| `JPOGTrexContentContainer.asset`, `JPOGTrexModInformation.asset` | The Dusk content container and mod information for the Dusk export. |
| `Package/` | README, changelog and icon of the Thunderstore package (referenced by the mod information). |
| `AssetBundles/` | Build output: the plugin DLL and the exported bundles. Only the `.meta` files are committed; the prefab references the DLL by the GUID in its `.meta`. |
| `Editor/` | The Unity editor script that builds this mod's bundles (menu JPOG > Build JPOGTrex bundles). |
| `Plugin~/` | The C# plugin. The `~` is on purpose: Unity ignores folders ending in `~`, so it doesn't try to compile the BepInEx code. |
| `AssetSources~/` | Source files (Blender) that Unity shouldn't import. |

## Building 🔨

1. Create `Plugin~/JPOGTrex.csproj.user` (gitignored, machine specific). It sets the game path, copies the netcode-patched DLL to `AssetBundles/` (where Unity finds the AI script for the prefab) and deploys the mod to your test setup. A minimal version:

   ```xml
   <Project>
     <PropertyGroup>
       <GameDirectory>/path/to/Lethal Company/</GameDirectory>
       <ManagedDirectory>$(GameDirectory)Lethal Company_Data/Managed/</ManagedDirectory>
     </PropertyGroup>
     <Target Name="CopyToUnity" AfterTargets="NetcodePatch">
       <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(MSBuildThisFileDirectory)../AssetBundles/" />
     </Target>
   </Project>
   ```

   To deploy, add a target after `CopyToUnity` that copies `AssetBundles/JPOGTrex.dll` and `AssetBundles/*.duskmod` to `BepInEx/plugins/347956-JPOGTrex/` of your r2modman profile, and the content bundle to an `Assets/` subfolder there (Dusk only loads content bundles from `Assets/`).

2. In `Plugin~`, run `dotnet tool restore` once, then `dotnet build`.
3. Build the bundles in Unity with **JPOG > Build JPOGTrex bundles** (`Editor/JPOGTrexBundleBuilder.cs`). It builds only this mod's bundles into `AssetBundles/`; the Asset Bundle Browser would build every bundle in the project into its one output folder. Run `dotnet build` again afterwards to deploy the new bundles.

> [!IMPORTANT]
> Build the DLL before opening the Unity project after a fresh clone. If the DLL is missing, Unity deletes its `.meta` file and the prefab loses its AI script.

### GitHub Actions ⚙️

- **Build and test** (`.github/workflows/build.yml`) runs on every push and pull request to `main`. It runs the xUnit projects in `Tests~/` (Debug and Release), builds the plugin with `CI=true`, which takes the game assemblies from the `LethalCompany.GameLibs.Steam` NuGet package, and packs the Thunderstore ZIP. The ZIP and the Debug DLL are uploaded as run artifacts.
- **GitHub release** (`.github/workflows/release.yml`) runs on every push to `main`. If the tag `v<Version>` for the `<Version>` in `Plugin~/JPOGTrex.csproj` doesn't exist yet, it tests, builds and checks the package, then creates the tag and a GitHub Release with the ZIP attached. It doesn't upload to Thunderstore.

  To release, raise `<Version>` in the csproj and the version in `JPOGTrexModInformation.asset` together, add a `## <version>` entry to `Package/CHANGELOG.md`, and rebuild and commit the bundles. The workflow stops if any of these is missing. You can also push a `v<version>` tag, or run the workflow by hand with an existing tag to retry.

CI builds can't build the Unity bundles. They package the bundles committed in `AssetBundles/`.

### Thunderstore Packages 📦

We have configured [JPOGTrex.csproj](/Plugin~/JPOGTrex.csproj) to build a Thunderstore package to [/Plugin~/Thunderstore/Packages/](/Plugin~/Thunderstore/Packages/) using [tcli](https://github.com/thunderstore-io/thunderstore-cli/wiki) each time we make a release build of our mod. A release build can be done for example from the command-line like this: `dotnet build -c release`. This will use configuration options from [thunderstore.toml](/Plugin~/Thunderstore/thunderstore.toml), so configure it for your own mod! Do note that we have not included a way to upload your mod to thunderstore via tcli in this project.


## Trouble shooting 🛠️
This section is mostly for myself to check how things should be made, or what to check if something isn't working as intended.  
It should give some tips when encountering problems in this project or your own.

### General 
- Build the plugin after code changes so the DLL in `AssetBundles/` is up to date; the prefab gets its scripts and properties from it
- Don't forget to add the prefab or other assets to the assetbundle in Unity
- Important events or manipulation of the model should be handles with networking. e.g. [ServerRpc] > [ClientRpc] > method()
  - If not done correctly it could cause syncing issues e.g. the body of the player being grabbed by the enemy for one player but laying on the ground for the other 
- Hit registration should be done server side: "[ServerRpc] > method()", to prevent latency of "[ServerRpc] > [ClientRpc] > method()" effecting the hit registration.

### Animations 
- Most animation in this project are started from the "any event". I am not sure if this is best practice, but it makes animating a lot easier.
- Coroutine can be can be used to execute code over frames. Usefull for waiting on animaitons to finish or executing logic at certain points during the animations.
- Enabling "Has Exit Time" makes animation B wait for animation A to finish in a transition of A > B.
- The T-Rex has a "mouthGrip" transform which is bind to the "mouthBone" of the model.
  - This way the mouthGrip's transform location can be updated in the code with the mouthBone's transform location, making the mouthGrip match the position of the T-Rex's mouth during animations.

## Credits 🗣️

- [EvaisaDev](https://github.com/EvaisaDev) - [LethalLib](https://github.com/EvaisaDev/LethalLib)  
- [Lordfirespeed](https://github.com/Lordfirespeed) - reference tcli usage in LethalLib  
- [Xilophor](https://github.com/Xilophor) - csproj files taken from Xilo's [mod templates](https://github.com/Xilophor/Lethal-Company-Mod-Templates)  
- [XuuXiao](https://github.com/XuuXiao/) - porting LC-ExampleEnemy for LC v50  
- [nomnomab](https://github.com/nomnomab) - [Lethal Company Project Patcher](https://github.com/nomnomab/lc-project-patcher) - used for the Unity Project  
- [AlbinoGeek](https://github.com/AlbinoGeek) - issue template  
- [HENDRIX-ZT2 ](https://github.com/HENDRIX-ZT2) & [AdventureT](https://github.com/AdventureT) - creating the blender plugin: [jpog-blender](https://github.com/HENDRIX-ZT2/jpog-blender) that is able to read ".tmd" files from the game: [Jurassic Park: Operation Genisis](https://en.wikipedia.org/wiki/Jurassic_Park:_Operation_Genesis)
- [Primal Carnage: Extinction](https://store.steampowered.com/app/321360/Primal_Carnage_Extinction/) - I use some of the sounds from their [T-Rex](https://youtu.be/VTaOiCKarqY?si=gmsnWuaOiKZl15iR)
