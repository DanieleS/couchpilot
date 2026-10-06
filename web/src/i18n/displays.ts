const en = {
  title: 'Displays',
  current: 'Current layout',
  virtual: 'virtual',
  primary: 'primary',
  presets: 'Presets',
  empty: 'Arrange the screens the way you want in Windows, then save them below: from then on they are one tap away.',
  testing: 'Testing',
  apply: 'Apply',
  presetAudio: 'Audio with this preset',
  rename: 'Rename',
  remove: 'Delete',
  saveCurrent: 'Save the current layout',
  testingFor: (s: number) => `Testing · ${s} s`,
  screens: (n: number) => `${n} displays`,
  confirm: {
    question: 'Can you see everything?',
    toPreset: (name: string) => `If you don't confirm, it switches back to «${name}» on its own.`,
    toPrevious: "If you don't confirm, it switches back to the previous layout on its own.",
    revert: 'Revert',
    keep: 'Keep',
  },
}

const it: typeof en = {
  title: 'Schermi',
  current: 'Configurazione attuale',
  virtual: 'virtuale',
  primary: 'principale',
  presets: 'Preset',
  empty: 'Sistema gli schermi come ti servono in Windows, poi salvali qui sotto: da quel momento li richiami con un tocco.',
  testing: 'In prova',
  apply: 'Applica',
  presetAudio: 'Audio con questo preset',
  rename: 'Rinomina',
  remove: 'Elimina',
  saveCurrent: 'Salva la configurazione attuale',
  testingFor: (s: number) => `In prova · ${s} s`,
  screens: (n: number) => `${n} schermi`,
  confirm: {
    question: 'Si vede tutto?',
    toPreset: (name: string) => `Se non confermi torno a «${name}» da solo.`,
    toPrevious: 'Se non confermi torno alla configurazione di prima da solo.',
    revert: 'Annulla',
    keep: 'Tieni',
  },
}

export default { en, it }
