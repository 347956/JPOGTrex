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