﻿/// <reference path="../../types-ragemp-client/index.d.ts" />
const dgram = require('dgram');
const client = dgram.createSocket('udp4');

let myTimer = null;

mp.events.add("PUREVOICE", (VoiceServerIP, VoiceServerPort, VoiceServerSecret, PlayerGUID, VoiceServerPluginVersion, VoiceClientPort) => {
    let buf = '2\tPUREVOICE\t' + VoiceServerIP + '\t' + VoiceServerPort + '\t' + VoiceServerSecret + '\t' + PlayerGUID + '\t' + VoiceServerPluginVersion + '\t';
    myTimer = setInterval(() => {
        client.send(buf, VoiceClientPort)
    }, 5000);
});

mp.events.add("LIPSYNC", (player: MpPlayer, animDict, animName) => {
    player.playFacialAnim(animName, animDict);
});
 

 
