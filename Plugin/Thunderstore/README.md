# JPOGTrex 🦖
This mod adds the T-rex from the game [Jurassic Park Operation Genesis](https://en.wikipedia.org/wiki/Jurassic_Park:_Operation_Genesis).

>[!NOTE]  ⚠️
> This is the first mod I made for Lethal Company. Release (v1.0.0) has the bare minimum features I think the T-rex should have to function as an enemy.
> The T-rex should function, but the mod is not done yet. You may experience some buggy behaviour from the T-rex. I'll try and keep the mod updated and fix bugs.


## Description 📃
JPOGTrex is a fully animated custom enemy that can spawn outside and will target players that are moving to much in it's vision.

### Behaviour ▶️
- Player spotting:
	- The Trex will spot players in it's line of sight that are moving (emotes, walking), once it's suspicion level reaches the treshhold, it will start chasing the last player seen moving.
	- If you hear the T-rex's footsteps, you're in range to be detected and targeted.
- Chasing:
	- The T-rex will chase the player last seen moving.
	- If the player gets to far from the T-rex, the T-rex pathfind to the last know position of the target player.
	- Going inside the facility or in the ship (and closing the door) will make the T-rex stop chasing you.
- Attacking:
	- The T-rex will grab and kill the player once it gets close enough.
- Eating:
	- If a player has been grabbed and killed, the T-rex will eat the body of the player.
- Killable:
	- The T-rex should have around the same HP as a giant (hitboxes/colliders need some improvement).
	- The T-rex can be killed by Earth Leviathan.

## Preview 👀
![Alt text](./preview.jpg?raw=true "JPOGTrex on Experimentation")


## future 🎯
In the future I would like to make add more behaviours to the T-rex, and make it feel more like a "neutral" enemy that attacks both players and entities.

### TODO 🛠️
- Make improvements to the audio (mainly the roars)
- Make the T-rex attack other entities
- Make the T-rex will eat other entities
- Make the T-rex's collision boxes better
- Make the hit detection of the T-rex better
- Fix the running animation for the T-rex. For some reason the T-rex's right foot has a strange animation when running.
- Add adrenaline effext to players that are seen moving by the T-rex
- Add more configurable properties to the T-rex (speed, vision range, hunger level, etc.)

## Source 🌐
The source code for the T-rex can be found on my [GitHub](https://github.com/347956/JPOGTrex). If you are making your own enemy for Lethal Company, feel free to take a look at the code and get some inspiration.

## JPOGT-rex assets 📦
All assets (model, textures, animations and some audio) are from the game Jurassic Park Opertion Genesis.  
I also used some audio form the T-rex from the game Primal Carnage: Extinction
