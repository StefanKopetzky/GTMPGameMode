/// <reference path="../GT-MP/typescripts/index.d.ts" />
API.onServerEventTrigger.connect(function (eventName, args) {
    var player = API.getLocalPlayer();
    if (eventName === "GTMPVOICE") {
        API.voiceEnable(args[0], args[1], args[2], args[3], args[4], args[5]);
        return;
    }
});
//# sourceMappingURL=clientEvents.js.map