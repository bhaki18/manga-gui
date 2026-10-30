const BaseProvider = require('./base');
const { fetchJson } = require('../utils/http');

class MangaDexProvider extends BaseProvider {
  constructor() {
    super('MangaDex', 'https://api.mangadex.org', {
      id: 'mangadex',
      type: 'manga',
      languages: ['all'],
      isBuiltIn: true
    });
    this.id = 'mangadex';
    this.type = 'manga';
    this.languages = ['all'];
    this.isBuiltIn = true;
    this.catalogCache = null;
  }

  _extractMangaItem(item, lang = null) {
    const attrs = item.attributes || {};
    const titleObj = attrs.title || {};
    const altTitles = Array.isArray(attrs.altTitles) ? attrs.altTitles : [];

    const findAlt = (code) => {
      const entry = altTitles.find(a => a && typeof a[code] === 'string' && a[code].trim().length > 0);
      return entry ? entry[code].trim() : null;
    };

    const romajiTitle = titleObj['ja-ro'] || titleObj['ko-ro'] || titleObj['zh-ro'] || findAlt('ja-ro') || findAlt('ko-ro') || Object.values(titleObj)[0] || '';
    const enTitle = titleObj.en || findAlt('en');
    const itTitle = titleObj.it || findAlt('it');

    const title = enTitle || itTitle || romajiTitle || 'Unknown Title';
    const altTitle = (romajiTitle && romajiTitle !== title) ? romajiTitle : (enTitle && enTitle !== title ? enTitle : '');

    const authorRel = item.relationships ? item.relationships.find(r => r.type === 'author') : null;
    const author = authorRel && authorRel.attributes ? authorRel.attributes.name : '';

    const tags = (attrs.tags || [])
      .slice(0, 3)
      .map(t => t.attributes && t.attributes.name && t.attributes.name.en)
      .filter(Boolean)
      .join(', ');

    return {
      id: item.id,
      title,
      altTitle,
      url: `${this.baseUrl}/manga/${item.id}`,
      author,
      genres: tags,
      source: 'mangadex',
      sourceName: 'MangaDex',
      language: lang && lang !== 'all' ? lang : 'en'
    };
  }

  /**
   * Search manga on MangaDex
   */
  async search(query, lang = null) {
    if (!query || !query.trim()) {
      return this.getCatalog(lang);
    }
    try {
      const cleanQuery = encodeURIComponent(query.trim());
      const url = `${this.baseUrl}/manga?title=${cleanQuery}&limit=25&hasAvailableChapters=true&includes[]=author&order[relevance]=desc`;
      const res = await fetchJson(url);

      if (!res || !Array.isArray(res.data)) return [];
      return res.data.map(item => this._extractMangaItem(item, lang));
    } catch (err) {
      return [];
    }
  }

  /**
   * Fetch top catalog from MangaDex
   */
  async getCatalog(lang = null) {
    if (this.catalogCache && this.catalogCache.length > 0) {
      return this.catalogCache;
    }
    try {
      const url = `${this.baseUrl}/manga?limit=80&order[followedCount]=desc&hasAvailableChapters=true&availableTranslatedLanguage[]=it&availableTranslatedLanguage[]=en&includes[]=author`;
      const res = await fetchJson(url);
      if (!res || !Array.isArray(res.data)) return [];
      const list = res.data.map(item => this._extractMangaItem(item, lang));
      if (list.length > 0) {
        this.catalogCache = list;
      }
      return list;
    } catch (err) {
      return [];
    }
  }

  async _fetchChaptersWithLangParam(mangaId, langParam) {
    const rawChapters = [];
    let offset = 0;
    const limit = 100;

    while (offset < 600) {
      const url = `${this.baseUrl}/chapter?manga=${mangaId}${langParam}&includeExternalUrl=0&includeEmptyPages=0&order[chapter]=asc&limit=${limit}&offset=${offset}`;
      const res = await fetchJson(url).catch(() => null);
      if (!res || !Array.isArray(res.data) || res.data.length === 0) break;

      rawChapters.push(...res.data);
      if (res.data.length < limit) break;
      offset += limit;
    }

    return rawChapters;
  }

  /**
   * Get all readable chapters in the specified language (preferring IT then EN)
   */
  async getChapters(mangaUrl, lang = null) {
    const mangaId = (mangaUrl.split('/manga/')[1] || mangaUrl).split(/[/?#]/)[0];
    if (!mangaId) return [];

    let rawData = [];
    const cleanLang = lang ? String(lang).toLowerCase().trim() : 'all';

    if (cleanLang && cleanLang !== 'all') {
      rawData = await this._fetchChaptersWithLangParam(mangaId, `&translatedLanguage[]=${cleanLang}`);
    }

    if (rawData.length === 0) {
      rawData = await this._fetchChaptersWithLangParam(mangaId, '&translatedLanguage[]=it&translatedLanguage[]=en');
    }

    if (rawData.length === 0) {
      rawData = await this._fetchChaptersWithLangParam(mangaId, '');
    }

    if (rawData.length === 0) return [];

    // Deduplicate by chapter number (preferring Italian > English > higher page count)
    const chapterMap = new Map();
    rawData.forEach((ch, idx) => {
      const cAttr = ch.attributes || {};
      if (cAttr.externalUrl || (typeof cAttr.pages === 'number' && cAttr.pages <= 0)) {
        return;
      }

      const rawNum = cAttr.chapter !== null && cAttr.chapter !== undefined && String(cAttr.chapter).trim() !== ''
        ? String(cAttr.chapter).trim()
        : String(idx + 1);
      const chapterLang = (cAttr.translatedLanguage || 'en').toLowerCase();
      const chapNumLabel = cAttr.chapter ? `Chapter ${cAttr.chapter}` : `Chapter ${idx + 1}`;
      const chapTitle = cAttr.title ? `: ${cAttr.title}` : '';

      const candidate = {
        id: ch.id,
        title: `[${chapterLang.toUpperCase()}] ${chapNumLabel}${chapTitle}`,
        url: `${this.baseUrl}/chapter/${ch.id}`,
        number: rawNum,
        lang: chapterLang,
        externalUrl: null,
        pagesCount: cAttr.pages || 0
      };

      const existing = chapterMap.get(rawNum);
      if (!existing) {
        chapterMap.set(rawNum, candidate);
      } else {
        const rank = (l) => (l === cleanLang ? 3 : (l === 'it' ? 2 : (l === 'en' ? 1 : 0)));
        if (rank(candidate.lang) > rank(existing.lang) ||
            (rank(candidate.lang) === rank(existing.lang) && candidate.pagesCount > existing.pagesCount)) {
          chapterMap.set(rawNum, candidate);
        }
      }
    });

    const deduped = Array.from(chapterMap.values());
    deduped.sort((a, b) => {
      const na = parseFloat(a.number);
      const nb = parseFloat(b.number);
      if (!isNaN(na) && !isNaN(nb)) return na - nb;
      return String(a.number).localeCompare(String(b.number));
    });

    return deduped;
  }

  /**
   * Get image pages for a specific chapter
   */
  async getChapterPages(chapterUrl) {
    const chapterId = (chapterUrl.split('/chapter/')[1] || chapterUrl).split(/[/?#]/)[0];

    const chData = await fetchJson(`${this.baseUrl}/chapter/${chapterId}`).catch(() => null);
    if (chData && chData.data && chData.data.attributes && chData.data.attributes.externalUrl) {
      const extUrl = chData.data.attributes.externalUrl;
      return {
        title: `Chapter ${chapterId}`,
        chapterUrl,
        externalUrl: extUrl,
        pages: []
      };
    }

    const serverUrl = `${this.baseUrl}/at-home/server/${chapterId}`;
    const res = await fetchJson(serverUrl);

    if (!res || !res.baseUrl || !res.chapter) {
      throw new Error('Unable to retrieve chapter images from MangaDex');
    }

    const host = res.baseUrl;
    const hash = res.chapter.hash;
    const files = (res.chapter.data && res.chapter.data.length > 0)
      ? res.chapter.data
      : (res.chapter.dataSaver || []);
    const mode = (res.chapter.data && res.chapter.data.length > 0) ? 'data' : 'data-saver';

    const pages = files.map(file => `${host}/${mode}/${hash}/${file}`);

    return {
      title: `Chapter ${chapterId}`,
      chapterUrl,
      pages
    };
  }
}

module.exports = MangaDexProvider;
