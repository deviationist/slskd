import './Dashboard.css';
import { usePullRefresh } from '../../lib/pullToRefresh';
import * as reports from '../../lib/reports';
import HistoricalStatistics from './HistoricalStatistics';
import SearchBar from './SearchBar';
import React, { useEffect, useMemo, useRef, useState } from 'react';

const HISTORY_RANGES = [
  { label: '24h', days: 1, buckets: 24 },
  { label: '7d', days: 7, buckets: 84 },
  { label: '30d', days: 30, buckets: 60 },
  { label: '90d', days: 90, buckets: 90 },
  { label: '180d', days: 180, buckets: 90 },
  { label: '1y', days: 365, buckets: 100 },
  { label: 'All', days: null, buckets: 100 },
];

const initialState = {
  summary: {},
  histogram: {},
  leaderboard: {
    upload: [],
    download: [],
  },
  directories: [],
  exceptions: {
    upload: {
      pareto: [],
      recent: [],
    },
    download: {
      pareto: [],
      recent: [],
    },
  },
};

const Dashboard = ({ server } = {}) => {
  const [loading, setLoading] = useState(true);
  const [historyLabel, setHistoryLabel] = useState('30d');
  const [historyTab, setHistoryTab] = useState(0);
  const [data, setData] = useState(initialState);
  // a pull moves the window's end up to now, and refetches without the loader
  // that would otherwise blank the page while it does
  const [refreshed, setRefreshed] = useState(0);
  const quiet = useRef(null);

  const historyParameters = useMemo(() => {
    const range =
      HISTORY_RANGES.find((r) => r.label === historyLabel) ?? HISTORY_RANGES[2];
    const now = new Date();
    return {
      buckets: range.buckets,
      end: now.toISOString(),
      start:
        range.days != null
          ? new Date(now - range.days * 86_400_000).toISOString()
          : new Date(0).toISOString(),
    };
  }, [historyLabel, refreshed]); // eslint-disable-line react-hooks/exhaustive-deps -- a pull's new end

  const fetchAll = async (parameters) => {
    const start = parameters.start ? new Date(parameters.start) : undefined;
    const end = new Date(parameters.end);

    if (!quiet.current) setLoading(true);

    const [
      summary,
      histogram,
      uploadLeaderboard,
      downloadLeaderboard,
      directories,
      uploadPareto,
      downloadPareto,
      uploadRecent,
      downloadRecent,
    ] = await Promise.all([
      reports.getSummary({ end, start }).catch((error) => {
        console.error(error);
        return {};
      }),
      reports
        .getHistogram({ buckets: parameters.buckets, end, start })
        .catch((error) => {
          console.error(error);
          return {};
        }),
      reports
        .getLeaderboard({ direction: 'Upload', end, start })
        .catch((error) => {
          console.error(error);
          return [];
        }),
      reports
        .getLeaderboard({ direction: 'Download', end, start })
        .catch((error) => {
          console.error(error);
          return [];
        }),
      reports.getTopDirectories({ end, start }).catch((error) => {
        console.error(error);
        return [];
      }),
      reports
        .getExceptionPareto({ direction: 'Upload', end, start })
        .catch((error) => {
          console.error(error);
          return [];
        }),
      reports
        .getExceptionPareto({ direction: 'Download', end, start })
        .catch((error) => {
          console.error(error);
          return [];
        }),
      reports
        .getExceptions({ direction: 'Upload', end, start })
        .catch((error) => {
          console.error(error);
          return [];
        }),
      reports
        .getExceptions({ direction: 'Download', end, start })
        .catch((error) => {
          console.error(error);
          return [];
        }),
    ]);

    setData({
      summary,
      histogram,
      leaderboard: { upload: uploadLeaderboard, download: downloadLeaderboard },
      directories,
      exceptions: {
        upload: { pareto: uploadPareto, recent: uploadRecent },
        download: { pareto: downloadPareto, recent: downloadRecent },
      },
    });
    setLoading(false);
  };

  useEffect(() => {
    const done = quiet.current;

    (async () => {
      await fetchAll(historyParameters);

      if (done) {
        quiet.current = null;
        done();
      }
    })();
  }, [historyParameters]); // eslint-disable-line react-hooks/exhaustive-deps

  usePullRefresh(
    () =>
      new Promise((resolve) => {
        quiet.current = resolve;
        setRefreshed((count) => count + 1);
      }),
  );

  return (
    <div className="dashboard">
      <SearchBar server={server} />
      <HistoricalStatistics
        activeTab={historyTab}
        directories={data.directories}
        exceptions={data.exceptions}
        histogram={data.histogram}
        historyEnd={historyParameters.end}
        historyLabel={historyLabel}
        historyRanges={HISTORY_RANGES}
        historyStart={historyParameters.start}
        leaderboard={data.leaderboard}
        loading={loading}
        onHistoryRangeSelect={(label) => setHistoryLabel(label)}
        onTabChange={setHistoryTab}
        summary={data.summary}
      />
    </div>
  );
};

export default Dashboard;
