const en = {
  back: 'Games',
  title: 'Statistics',
  period: 'Period',
  ranges: {
    week: 'Week',
    month: 'Month',
    year: 'Year',
  },
  thisWeek: 'This week',
  lastWeek: 'Last week',
  failed: {
    title: 'Can\'t read the statistics',
    hint: 'The PC and the phone need to be on the same network.',
  },
  previous: 'Previous period',
  next: 'Next period',
  untracked: (since: string | null) =>
    since
      ? `GameActivity has sessions from ${since}: there are none for this period. Playnite keeps only the totals, which you'll find below.`
      : 'GameActivity has no sessions yet. Playnite keeps only the totals, which you\'ll find below.',
  noActivity: 'Install GameActivity in Playnite to see when you play: Playnite itself keeps only totals, which you find below.',
  played: 'Played',
  // Compared with the period before: "+2 h 10 min vs last week at this point".
  than: {
    week: { running: 'last week at this point', done: 'the week before' },
    month: { running: 'last month at this point', done: 'the month before' },
    year: { running: 'last year at this point', done: 'the year before' },
  },
  same: (than: string) => `Same as ${than}`,
  diff: (diff: string, than: string) => `${diff} vs ${than}`,
  session: 'session',
  sessions: 'sessions',
  average: (time: string) => `average ${time}`,
  top: 'Most played',
  achievements: 'Achievements',
  fromSuccessStory: 'from SuccessStory',
  unlockedOne: 'unlocked',
  unlockedMany: 'unlocked',
  noAchievements: 'Nothing in this period.',
  library: 'Library',
  fromPlaynite: 'from Playnite',
  hours: (h: string) => `${h} h`,
  playedTotal: 'played in total',
  games: (installed: string) => `games, ${installed} installed`,
  neverPlayed: 'installed and never launched',
  playedThisYear: 'played this year',
  resume: 'Pick up again',
  resumeHint: 'installed, untouched for 3 months',
  bars: {
    // Week bars run Monday to Sunday, as the PC sends them.
    weekdays: ['M', 'T', 'W', 'T', 'F', 'S', 'S'],
    months: ['J', 'F', 'M', 'A', 'M', 'J', 'J', 'A', 'S', 'O', 'N', 'D'],
    hint: 'Tap a bar for details',
    label: 'Playtime',
  },
}

const it: typeof en = {
  back: 'Giochi',
  title: 'Statistiche',
  period: 'Periodo',
  ranges: {
    week: 'Settimana',
    month: 'Mese',
    year: 'Anno',
  },
  thisWeek: 'Questa settimana',
  lastWeek: 'La settimana scorsa',
  failed: {
    title: 'Non riesco a leggere le statistiche',
    hint: 'Il PC e il telefono devono essere sulla stessa rete.',
  },
  previous: 'Periodo precedente',
  next: 'Periodo successivo',
  untracked: (since: string | null) =>
    since
      ? `GameActivity ha sessioni dal ${since}: per questo periodo non ce ne sono. Playnite tiene solo i totali, che trovi qui sotto.`
      : 'GameActivity non ha ancora sessioni. Playnite tiene solo i totali, che trovi qui sotto.',
  noActivity: 'Installa GameActivity in Playnite per vedere quando giochi: Playnite tiene solo i totali, che trovi qui sotto.',
  played: 'Giocate',
  than: {
    week: { running: 'della settimana scorsa a questo punto', done: 'della settimana prima' },
    month: { running: 'del mese scorso a questo punto', done: 'del mese prima' },
    year: { running: "dell'anno scorso a questo punto", done: "dell'anno prima" },
  },
  same: (than: string) => `Come quelle ${than}`,
  diff: (diff: string, than: string) => `${diff} rispetto a quelle ${than}`,
  session: 'sessione',
  sessions: 'sessioni',
  average: (time: string) => `media ${time}`,
  top: 'Più giocati',
  achievements: 'Obiettivi',
  fromSuccessStory: 'da SuccessStory',
  unlockedOne: 'sbloccato',
  unlockedMany: 'sbloccati',
  noAchievements: 'Niente in questo periodo.',
  library: 'Libreria',
  fromPlaynite: 'da Playnite',
  hours: (h: string) => `${h} h`,
  playedTotal: 'giocate in tutto',
  games: (installed: string) => `giochi, ${installed} installati`,
  neverPlayed: 'installati e mai avviati',
  playedThisYear: "giocati quest'anno",
  resume: 'Da riprendere',
  resumeHint: 'installati, fermi da 3 mesi',
  bars: {
    weekdays: ['L', 'M', 'M', 'G', 'V', 'S', 'D'],
    months: ['G', 'F', 'M', 'A', 'M', 'G', 'L', 'A', 'S', 'O', 'N', 'D'],
    hint: 'Tocca una barra per il dettaglio',
    label: 'Tempo di gioco',
  },
}

export default { en, it }
