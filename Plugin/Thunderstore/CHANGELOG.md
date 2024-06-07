## 1.0.6
- Changes to the roar aduio clips: 
	- The roar is now divided into multiple clips that are played on different audio sources. This should make it so that only the "main" part of the roar audio clip is loadn and/or heard from further away.
- Fixed the bite sound effect:
	- The Bite audio clip should now be played correctly when the T-rex begins the grab aniamtion. 

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