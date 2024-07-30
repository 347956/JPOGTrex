# JPOGTrex 🦖
This mod adds the T-Rex from the game [Jurassic Park Operation Genesis](https://en.wikipedia.org/wiki/Jurassic_Park:_Operation_Genesis).

> ⚠️  
> This is the first mod I made for Lethal Company. Release (v1.0.0) has the bare minimum features I think the T-Rex should have to function as an enemy.
> The T-Rex should function, but the mod is not done yet. You may experience some buggy behaviour from the T-Rex. I'll try and keep the mod updated and fix bugs.


## Description 📃
JPOGTrex is a fully animated custom enemy that can spawn outside and will target players that are moving to much in it's vision.

### Behaviour ▶️
- Player spotting:
	- The Trex will spot players in it's line of sight that are moving (emotes, walking), once it's suspicion level reaches the treshold, it will start chasing the last player seen moving.
	- When you get to close to the T-Rex you will also be spotted.
	- If you hear the T-Rex's footsteps, you're in range to be detected and targeted.
- Chasing:
	- The T-Rex will chase the player last seen moving.
	- If the player gets to far from the T-Rex, the T-Rex will go to the last know position of the target player.
	- Going inside the facility or in the ship (and closing the door) will make the T-Rex stop chasing you.
- Attacking:
	- The T-Rex will grab and kill the player once it gets close enough.
- Eating:
	- If a player has been grabbed and killed, the T-Rex will eat the body of the player.
- Killable:
	- The T-Rex should have around the same HP as a giant (hitboxes/colliders need some improvement).
	- The T-Rex can be killed by Earth Leviathan.

## Feedback 🗣️
Feel free to leave feedback and/or bugs you encounter on the discord thread for JPOG T-Rex or by Creating a GitHub issue!  
- [Lethal Company Modding discord](https://discord.com/channels/1168655651455639582/1267152262602555473):  
	[Modding] > [mod-releases] > [JPOG T-Rex 🦖] 
- [GitHub Issues](https://github.com/347956/JPOGTrex/issues)


## Preview 👀
![JPOGTrex on experimentation](https://i.imgur.com/mw49lHV.jpeg)

## Future 🎯
In the future I would like to make add more behaviours to the T-Rex, and make it feel more like a "neutral" enemy that attacks both players and entities.

### Other projects 💭
- Create a custom moon with JPOG assets.
- Add other JPOG dinosaurs.

### TODO 🛠️
- Make improvements to the audio (mainly the roars) ✅
- Make the T-Rex attack other entities
- Make the T-Rex will eat other entities
- Make the T-Rex's collision boxes better
- Make the hit detection of the T-Rex better ✅
- Fix the running animation for the T-Rex. For some reason the T-Rex's right foot has a strange animation when running.
- Fix the T-Rex's "MouthGrip" not being set propperly. ✅
- Add adrenaline effect to players that are seen moving by the T-Rex
- Add more configurable properties to the T-Rex (speed, vision range, hunger level, etc.)

## Source 🌐
The source code for the T-Rex can be found on my [GitHub](https://github.com/347956/JPOGTrex). If you are making your own enemy mod for Lethal Company, feel free to take a look at my code for inspiration.

## JPOGT-rex assets 📦
All assets (model, textures, animations and some audio) are from the game:
[Jurassic Park Operation Genesis](https://en.wikipedia.org/wiki/Jurassic_Park:_Operation_Genesis)  
I also used some audio form the T-Rex from the game:
[Primal Carnage: Extinction](https://store.steampowered.com/app/321360/Primal_Carnage_Extinction/)
