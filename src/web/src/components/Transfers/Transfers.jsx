import './Transfers.css';
import * as transfersLibrary from '../../lib/transfers';
import AppContext from '../AppContext';
import { LoaderSegment, PlaceholderSegment } from '../Shared';
import TransferGroup from './TransferGroup';
import TransfersHeader from './TransfersHeader';
import React, { useContext, useEffect, useMemo, useState } from 'react';
import { toast } from 'react-toastify';

const sortStorageKey = (direction) => `slskd-transfers-${direction}-sort`;

/**
 * The order chosen in this browser for this direction, or null for none.
 *
 * Null rather than a default is the useful answer: it is what lets the
 * configured `transfers.<direction>.default_sort` apply to a browser that has
 * never touched the control. Validating the value is left to `resolveSort`,
 * which has to weigh it against the configured one anyway.
 *
 * Guarded because localStorage throws outright in a browser with site data
 * blocked, and this runs on the first render of the page: an exception here
 * would cost the whole list rather than a preference.
 */
const readStoredSort = (direction) => {
  try {
    return window.localStorage.getItem(sortStorageKey(direction));
  } catch {
    return null;
  }
};

const Transfers = ({ direction, server }) => {
  // options arrive over the application hub, so this is {} until it connects
  // and changes again whenever the configuration is edited
  const { options } = useContext(AppContext) ?? {};
  const [connecting, setConnecting] = useState(true);
  const [transfers, setTransfers] = useState([]);
  const [storedSort, setStoredSort] = useState(() => readStoredSort(direction));

  const [retrying, setRetrying] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [removing, setRemoving] = useState(false);

  const fetch = async () => {
    try {
      const response = await transfersLibrary.getAll({ direction });
      setTransfers(response);
    } catch (error) {
      console.error(error);
      toast.error(error?.response?.data ?? error?.message ?? error);
    }
  };

  useEffect(() => {
    setConnecting(true);

    const init = async () => {
      await fetch();
      setConnecting(false);
    };

    init();
    const interval = window.setInterval(fetch, 1_000);

    return () => {
      clearInterval(interval);
    };
  }, [direction]); // eslint-disable-line react-hooks/exhaustive-deps

  useMemo(() => {
    // this is used to prevent weird update issues if switching
    // between uploads and downloads.  useEffect fires _after_ the
    // prop 'direction' updates, meaning there's a flash where the
    // screen contents switch to the new direction for a brief moment
    // before the connecting animation shows.  this memo fires the instant
    // the direction prop changes, preventing this flash.
    setConnecting(true);

    // the preference is per direction, and this component is reused across
    // both -- see above -- so it has to be re-read rather than initialised once
    setStoredSort(readStoredSort(direction));
  }, [direction]); // eslint-disable-line react-hooks/exhaustive-deps

  const changeSort = (value) => {
    setStoredSort(value);

    try {
      window.localStorage.setItem(sortStorageKey(direction), value);
    } catch {
      // a preference that cannot be stored is still worth honouring for this
      // visit, and there is nothing here to tell the operator about
    }
  };

  // the direction prop is singular ('download'/'upload') and so is the options
  // key, which is what allows the lookup to be by direction rather than by a
  // mapping that would have to be kept in step with it
  const configuredSort = options?.transfers?.[direction]?.defaultSort;
  const sort = transfersLibrary.resolveSort(storedSort, configuredSort);

  // whether the server will hand a downloaded file back over the API. the
  // button is hidden rather than shown and refused: options arrive over the
  // hub, so this is false until it connects, which errs towards not offering
  // something that would not work
  const retrievalEnabled = options?.remoteFileRetrieval === true;

  const sorted = useMemo(
    () => transfersLibrary.sortTransfers(transfers, sort),
    [sort, transfers],
  );

  const retry = async ({ file, suppressStateChange = false }) => {
    const { filename, size, username } = file;

    try {
      if (!suppressStateChange) setRetrying(true);
      await transfersLibrary.download({
        files: [{ filename, size }],
        username,
      });
      if (!suppressStateChange) setRetrying(false);
    } catch (error) {
      console.error(error);
      toast.error(error?.response?.data ?? error?.message ?? error);
      if (!suppressStateChange) setRetrying(false);
    }
  };

  const retryAll = async (transfersToRetry) => {
    setRetrying(true);
    await Promise.all(
      transfersToRetry.map((file) =>
        retry({ file, suppressStateChange: true }),
      ),
    );
    setRetrying(false);
  };

  const cancel = async ({ file, suppressStateChange = false }) => {
    const { id, username } = file;

    try {
      if (!suppressStateChange) setCancelling(true);
      await transfersLibrary.cancel({ direction, id, username });
      if (!suppressStateChange) setCancelling(false);
    } catch (error) {
      console.error(error);
      toast.error(error?.response?.data ?? error?.message ?? error);
      if (!suppressStateChange) setCancelling(false);
    }
  };

  const cancelAll = async (transfersToCancel) => {
    setCancelling(true);
    await Promise.all(
      transfersToCancel.map((file) =>
        cancel({ file, suppressStateChange: true }),
      ),
    );
    setCancelling(false);
  };

  /**
   * Removes the record of a transfer, and returns what the server did with it.
   *
   * Whether the file goes too is the server's decision, from
   * `transfers.download.delete_file_on_removal`. A removal that deleted nothing
   * answers 204 and has nothing to report; one that deleted something answers
   * with the outcome, and `removeAll` says so.
   */
  const remove = async ({ file, suppressStateChange = false }) => {
    const { id, username } = file;

    try {
      if (!suppressStateChange) setRemoving(true);
      const response = await transfersLibrary.cancel({
        direction,
        id,
        remove: true,
        username,
      });

      if (!suppressStateChange) setRemoving(false);
      return { data: response?.data, ok: true };
    } catch (error) {
      console.error(error);
      toast.error(error?.response?.data ?? error?.message ?? error);
      if (!suppressStateChange) setRemoving(false);
      return { error, ok: false };
    }
  };

  const removeAll = async (transfersToRemove) => {
    setRemoving(true);
    const results = await Promise.all(
      transfersToRemove.map((file) =>
        remove({ file, suppressStateChange: true }),
      ),
    );
    setRemoving(false);

    // this is the header's bulk remove, and with the option on it deletes files
    // across every card on the page. a deletion is never silent, wherever it
    // was asked for
    const summary = transfersLibrary.summariseDeletions(results);

    if (summary) {
      toast[summary.kind](summary.message);
    }
  };

  if (connecting) {
    return <LoaderSegment />;
  }

  return (
    <>
      <TransfersHeader
        cancelling={cancelling}
        direction={direction}
        onCancelAll={cancelAll}
        onRemoveAll={removeAll}
        onRetryAll={retryAll}
        onSortChange={changeSort}
        removing={removing}
        retrying={retrying}
        server={server}
        sort={sort}
        transfers={transfers}
      />
      {transfers.length === 0 ? (
        <PlaceholderSegment
          caption={`No ${direction}s to display`}
          icon={direction}
        />
      ) : (
        sorted.map((user) => (
          <TransferGroup
            cancel={cancel}
            cancelAll={cancelAll}
            direction={direction}
            key={user.username}
            remove={remove}
            removeAll={removeAll}
            retrievalEnabled={retrievalEnabled}
            retry={retry}
            retryAll={retryAll}
            user={user}
          />
        ))
      )}
    </>
  );
};

export default Transfers;
