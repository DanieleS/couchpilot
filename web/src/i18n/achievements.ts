const en = {
  sheetTitle: (game: string) => `${game} achievements`,
  count: (total: number) => `of ${total} achievements`,
  all100: 'all of them!',
  which: 'Show',
  filters: {
    all: 'All',
    unlocked: 'Unlocked',
    locked: 'To do',
  },
  none: 'No achievements for this game yet.',
  allDone: 'Nothing left: you have them all.',
  noneYet: 'None yet: the next session is the one.',
  hidden: 'Hidden achievement',
  reveal: 'Show anyway',
  // SuccessStory's own bands, for the few that deserve a word.
  rarity: {
    ultraRare: 'Ultra rare',
    rare: 'Rare',
    uncommon: 'Uncommon',
  },
  ofPlayers: (percent: string) => `${percent}% of players`,
  unlocked: 'Unlocked',
  today: (time: string) => `Today at ${time}`,
  yesterday: (time: string) => `Yesterday at ${time}`,
}

const it: typeof en = {
  sheetTitle: (game: string) => `Obiettivi di ${game}`,
  count: (total: number) => `di ${total} obiettivi`,
  all100: 'tutti!',
  which: 'Quali',
  filters: {
    all: 'Tutti',
    unlocked: 'Sbloccati',
    locked: 'Da fare',
  },
  none: 'Ancora nessun obiettivo per questo gioco.',
  allDone: 'Niente da fare: li hai tutti.',
  noneYet: 'Ancora nessuno: la prossima partita è quella buona.',
  hidden: 'Obiettivo nascosto',
  reveal: 'Mostra lo stesso',
  rarity: {
    ultraRare: 'Rarissimo',
    rare: 'Raro',
    uncommon: 'Non comune',
  },
  ofPlayers: (percent: string) => `${percent}% dei giocatori`,
  unlocked: 'Sbloccato',
  today: (time: string) => `Oggi alle ${time}`,
  yesterday: (time: string) => `Ieri alle ${time}`,
}

export default { en, it }
