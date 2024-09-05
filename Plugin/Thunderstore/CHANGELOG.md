## 1.1.2

The T-rex should now be able to attack enemies it collides with. Hopefully this makes it feel a bit more alive.
- The anaimation played depends on the size of the enemy.



## 1.1.1
JPOGTrex patched for v60/v62 (It was already working on v60/v62 but just to be sure).  
A few fixes with the help of [Xu Xiaolan](https://thunderstore.io/c/lethal-company/p/XuXiaolan/)

### Changes
- The T-Rex getting stuck on terrain:
	- The Nav Mesh Agent's radius has been reduced, this should make the T-Rex get stuck on terrain less often.
- Slowing down when approaching the target player:
	- The T-Rex now uses a "Network Transform" which should help it close in on players.
- Configuration:
	- Custom Spawn rates can now be set per moon including custom moons!
	- This Requires [Lethal Level Loader](https://thunderstore.io/c/lethal-company/p/IAmBatby/LethalLevelLoader/) and can be configured the same way as you would for example: [TheGiantSpecimens](https://thunderstore.io/c/lethal-company/p/XuXiaolan/TheGiantSpecimens/).
	- E.g. Setting the spawnweight for a custom level can by adding the level name to the list like this: ",Orion:20".
- Terminal/Bestiary:
	- You can now view info about the T-Rex in the terminal by typing "trex".
	- A video is also played in the terminal.
- Killing the player
	- The T-Rex can now also snap the player in half (like the barber) when killing them with it's bite attack. 

As always feel free to reach out if you encounter bugs or have suggestions.

## 1.1.0
Mostly added new configurations and a fix for the suspicion meter of the T-Rex.
- Suspicion:
	- Fixed the T-Rex's suspicion meter. The T-Rex already had a suspicion meter but it would decrease it's suspicion level too early and fast.
- Configuration:
	- New configurations have been added for the T-Rex's behavior:
		- Vision Range length
		- Vision Range Width
		- Max Suspicion Level
		- Suspicion Increment
		- Suspicion Decrement
		- Suspicion Decrease Time
		
These changes should make the T-Rex less "blind" as the suspicion is no longer decreased too early after just increasing. The configurations will also help as the T-Rex's vision can now be adjusted for those who want it to be more "aggresive" or dangerous.

**NOTE:** Feel free to create/open an issue on GitHub about problems you encounter or leave a comment in the JPOG-TRex thread on discord in case something isn't working / too buggy.

## 1.0.9
- Animations:
	- When the T-Rex is sniffing/roaring, the breathing animation will continue making it feel a bit less stiff / T pose.
- Behavior:  
(**These two changes should also fix getting killed inside of the ship through the wall**)
	- **ONE:** Changed the Hit detection logic from a fixed "kill zone" hitbox in front of the T-Rex to a collider that moves with the head during the grabbing animations and detects if it connects with a player.  
		- This should make the grab attack feel a bit smoother and more natural compared to the previous method. It should also give you more and/or a fairer chance to survive the grab attack (e.g. The T-Rex missing).
	- **TWO:** Fixed the Chase logic to stop the T-Rex from chasing you in the ship.  
		- If you run inside of the ship when the T-Rex is chasing you, it will immediately stop the chase and return to its roaming/searching state (similar to the forest giant).
- GitHub Issue:
	- Added logic that enables and disables autobraking during the chase and grab phase, this should help the T-Rex close in on its target.
		- The "Auto Braking" setting for the Nav Mesh Agent in Unity causes the T-Rex to slow down when it reaches its target destination. Enabling "Auto Braking" causes the T-Rex to slowdown prematurely, disabling "Auto Braking" causes the T-Rex to overshoot the target and orbit/slide/drift around it.
	
**NOTE:** Feel free to create/open an issue on GitHub about problems you encounter or in case something isn't working / too buggy.

## 1.0.8
- V55:
	- I have tested the T-Rex a bit myself and so far it works "normal" (just as before v55) and I don't think there are any changes that would break the T-Rex.
- Made the T-Rex more configurable:
	- Added Configuration for the default speed of the T-Rex.
	- Added Configuration for the max amount of T-rexes that can spawn naturally.
	- Added Configuration options for the spawn weight of the T-Rex (now selectable per moon).
	- Added basics for Syncing the configurations between players (Host - Client).
- Increased the Default speed of the T-Rex from 4 to 6.
	- This should make the T-Rex a bit faster and better at keeping up with players.
- Added logic for the T-Rex to stop chasing when the height difference with the targeted player is too high.
	- This should make the T-Rex feel a bit more responsive and cause it to go back to it's searching state when it cannot reach the player.
	- This is a solution but difficult to fine-tune as it could also cause the T-Rex to stop chasing immediatly after it targets a player already on higher elevation.

**NOTE:** Feel free to create/open an issue on GitHub about problems you encounter or in case something isn't working / too buggy.


## 1.0.7
- Changed the in game name of JPOGTrex to "T-Rex":
	- When scanning the T-Rex it should now be called "T-Rex" instead of "JPOGTrex".
- Changed the T-Rex agro on hit:
	- The T-Rex should now start chasing the player that hit it.
	- During a chase this should cause the T-Rex to switch targets.
	- **Careful:** The T-Rex will agro instantly and start chasing. Shovel attacks are not recommended.
	- The T-Rex should not start chasing the player if it has grabbed a player or is eating a player.
- Fixed Death Animation:
	- When the T-Rex's HP becomes 0 or less it should now instantly die.
	- No more delayed death animation.
	- **Note:** If The T-Rex dies while grabbing/eating a player, the body will be recoverable with the teleporter.  
	  *(I could also try and make it so that it drops/"ungrips" the body. But the TP method also makes sense. How else could you properly recover a body from a dead T-Rex's clutched jaws anyway?)**

## 1.0.6
- Changes to the roar audio clips: 
	- The roar is now divided into multiple clips that are played on different audio sources. This should make it so that only the "main" part of the roar audio clip is loud and/or heard from further away.
- Fixed the bite sound effect:
	- The Bite audio clip should now be played correctly when the T-Rex begins the grab animation. 

## 1.0.5
- Changed how the T-Rex MouthGrip is set:
	- **OLD**: The MouthGrip used to be set by being updated to match the MouthBone's location
	- **NEW**: The MouthGrip is now a Child of the MouthBone (in the prefab) and should thus inherit it's position
	- **Result**: Bug where the MouthGrip is not set properly when the T-Rex spawns should now be fixed. Now multiple T-rexes should be able to coexist without the body of the player teleporting to the first T-Rex's MouthGrip.
- Fixed preview image


## 1.0.4

- Fixed package

## 1.0.0

- Initial release