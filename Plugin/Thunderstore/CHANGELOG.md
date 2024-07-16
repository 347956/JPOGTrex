## 1.0.8
- V55:
	- I have tested the T-rex a bit myself and so far it works "normal" (just as before v55) and I don't think there are any changes that would break the T-rex.
- Made the T-rex more configurable:
	- Added Configuration for the default speed of the T-rex.
	- Added Configuration for the max amount of T-rexes that can spawn naturally.
	- Added Configuration options for the spawn weight of the T-rex (now selectable per moon).
	- Added basics for Syncing the configurations between players (Host - Client).
- Increased the Default speed of the T-rex from 4 to 6.
	- This should make the T-rex a bit faster and better at keeping up with players.
- Added logic for the T-rex to stop chasing when the height difference with the targeted player is too high.
	- This should make the T-rex feel a bit more responsive and cause it to go back to it's searching state when it cannot reach the player.
	- This is a solution but difficult to fine-tune as it could also cause the T-rex to stop chasing immediatly after it targets a player already on higher elevation.

**NOTE:** Feel free to create/open an issue on Github about problems you encounter or in case something isn't working / too buggy.


## 1.0.7
- Changed the in game name of JPOGTrex to "T-rex":
	- When scanning the T-rex it should now be called "T-rex" instead of "JPOGTrex".
- Changed the T-rex agro on hit:
	- The T-rex should now start chasing the player that hit it.
	- During a chase this should cause the T-rex to switch targets.
	- **Careful:** The T-rex will agro instantly and start chasing. Shovel attacks are not recommended.
	- The T-rex should not start chasing the player if it has grabbed a player or is eating a player.
- Fixed Death Animation:
	- When the T-rex's HP becomes 0 or less it should now instantly die.
	- No more delayed death animation.
	- **Note:** If The T-rex dies while grabbing/eating a player, the body will be recoverable with the teleporter.  
	  *(I could also try and make it so that it drops/"ungrips" the body. But the TP method also makes sense. How else could you properly recover a body from a dead T-rex's clutched jaws anyway?)**

## 1.0.6
- Changes to the roar audio clips: 
	- The roar is now divided into multiple clips that are played on different audio sources. This should make it so that only the "main" part of the roar audio clip is loud and/or heard from further away.
- Fixed the bite sound effect:
	- The Bite audio clip should now be played correctly when the T-rex begins the grab animation. 

## 1.0.5
- Changed how the T-rex MouthGrip is set:
	- **OLD**: The MouthGrip used to be set by being updated to match the MouthBone's location
	- **NEW**: The MouthGrip is now a Child of the MouthBone (in the prefab) and should thus inherit it's position
	- **Result**: Bug where the MouthGrip is not set properly when the T-rex spawns should now be fixed. Now multiple T-rexes should be able to coexist without the body of the player teleporting to the first T-rex's MouthGrip.
- Fixed preview image


## 1.0.4

- Fixed package

## 1.0.0

- Initial release