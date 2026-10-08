const en = {
  title: 'Control',
  fullScreen: 'Full screen',
  off: {
    title: 'Touchpad and keyboard are off',
    // Split around the optional part about paired phones.
    before: 'Turn them on in Playnite: Add-ons → Extension settings → CouchPilot → “Let the phone use the PC\'s mouse and keyboard”',
    pairing: ', and under Phones tick “Mouse and keyboard” for this phone',
    after: '.',
  },
  connecting: {
    title: 'Connecting to the PC…',
    text: 'The PC and the phone must be on the same network.',
  },
  keyboard: 'Keyboard',
  controllers: 'Controllers',
  shortcuts: 'Shortcuts',
  unlock: {
    title: 'The PC is locked',
    text: 'Unlock it from here, no keyboard needed.',
    button: 'Unlock the PC',
    unlocking: 'Unlocking…',
  },
  pad: {
    move: 'One finger moves, a tap clicks',
    scroll: 'Two fingers scroll, a tap right-clicks',
    drag: 'Tap, then hold to drag',
    dragging: 'Dragging',
    left: 'Left',
    right: 'Right',
  },
  keys: {
    del: 'Del',
    pageUp: 'PgUp',
    pageDown: 'PgDn',
    up: 'Up',
    down: 'Down',
    left: 'Left',
    right: 'Right',
    field: 'Type on the PC',
    done: 'Done',
  },
  controller: {
    plain: (slot: number) => `Controller ${slot}`,
    wired: (slot: number) => `Controller ${slot}, wired`,
    battery: (slot: number, level: 'empty' | 'low' | 'medium' | 'full') =>
      `Controller ${slot}, battery ${({ empty: 'empty', low: 'low', medium: 'half full', full: 'full' })[level]}`,
    cable: 'wired',
  },
}

const it: typeof en = {
  title: 'Controllo',
  fullScreen: 'Schermo intero',
  off: {
    title: 'Touchpad e tastiera sono spenti',
    before: 'Accendili in Playnite: Componenti aggiuntivi → Impostazioni estensioni → CouchPilot → «Permetti al telefono di usare mouse e tastiera del PC»',
    pairing: ', e in Telefoni spunta «Mouse e tastiera» per questo telefono',
    after: '.',
  },
  connecting: {
    title: 'Mi collego al PC…',
    text: 'Il PC e il telefono devono essere sulla stessa rete.',
  },
  keyboard: 'Tastiera',
  controllers: 'Controller',
  shortcuts: 'Scorciatoie',
  unlock: {
    title: 'Il PC è bloccato',
    text: 'Sbloccalo da qui, senza tastiera.',
    button: 'Sblocca il PC',
    unlocking: 'Sblocco…',
  },
  pad: {
    move: 'Un dito muove, un tocco clicca',
    scroll: 'Due dita scorrono, toccando fai clic destro',
    drag: 'Tocca e poi tieni premuto per trascinare',
    dragging: 'Trascinamento',
    left: 'Sinistro',
    right: 'Destro',
  },
  keys: {
    del: 'Canc',
    pageUp: 'Pag↑',
    pageDown: 'Pag↓',
    up: 'Su',
    down: 'Giù',
    left: 'Sinistra',
    right: 'Destra',
    field: 'Scrivi sul PC',
    done: 'Fine',
  },
  controller: {
    plain: (slot: number) => `Controller ${slot}`,
    wired: (slot: number) => `Controller ${slot}, via cavo`,
    battery: (slot: number, level: 'empty' | 'low' | 'medium' | 'full') =>
      `Controller ${slot}, batteria ${({ empty: 'scarica', low: 'bassa', medium: 'a metà', full: 'carica' })[level]}`,
    cable: 'cavo',
  },
}

export default { en, it }
