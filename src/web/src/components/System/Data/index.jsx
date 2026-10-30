import { deleteSavedAnalyses, getSavedAnalyses } from '../../../lib/player/api';
import { clearCompleted } from '../../../lib/transfers';
import React, { useEffect, useState } from 'react';
import { toast } from 'react-toastify';
import { Button, Divider, Header, Icon } from 'semantic-ui-react';

const clear = async ({ direction, setState }) => {
  setState(true);
  await clearCompleted({ direction });
  setState(false);
  toast.success(`Completed ${direction}s cleared!`);
};

const plural = (count) => `${count} download${count === 1 ? '' : 's'}`;

// only where analysis is on: without it there is nothing saved
export const SavedAnalyses = () => {
  const [saved, setSaved] = useState(null);
  const [clearing, setClearing] = useState(false);

  useEffect(() => {
    const load = async () => {
      try {
        setSaved(await getSavedAnalyses());
      } catch {
        setSaved(null);
      }
    };

    load();
  }, []);

  if (!saved?.configured) {
    return null;
  }

  const onClear = async () => {
    setClearing(true);

    try {
      const { downloads } = await deleteSavedAnalyses();
      setSaved({ ...saved, downloads: 0 });
      toast.success(`Audio analysis cleared for ${plural(downloads)}`);
    } catch (error) {
      toast.error(`Could not clear the audio analysis: ${error.message}`);
    } finally {
      setClearing(false);
    }
  };

  return (
    <>
      <Header
        as="h3"
        className="transfer-header"
      >
        Audio Analysis
      </Header>
      <Divider />
      <p>
        Each download's audio analysis is saved for good once made: the audio
        does not change, and a new version of the analyser deletes what the old
        one made by itself. Clear it if the analyser's rules have changed
        without its version saying so; each download is then analysed again the
        next time it is shown. Saved for {plural(saved.downloads)}.
      </p>
      <Button
        disabled={saved.downloads === 0}
        loading={clearing}
        onClick={onClear}
        primary
      >
        <Icon name="trash alternate" />
        Clear Audio Analysis
      </Button>
    </>
  );
};

const Data = () => {
  const [up, setUp] = useState(false);
  const [down, setDown] = useState(false);

  return (
    <div>
      <Header
        as="h3"
        className="transfer-header"
      >
        Transfer Data
      </Header>
      <Divider />
      <p>
        <span>
          The Uploads and Downloads pages can become unresponsive if too many
          transfers are displayed. If you're having trouble with either page,
          try using the buttons below to remove completed transfers.
        </span>
      </p>
      <Button
        loading={up}
        onClick={() => clear({ direction: 'upload', setState: setUp })}
        primary
      >
        <Icon name="trash alternate" />
        Clear All Completed Uploads
      </Button>
      <Button
        loading={down}
        onClick={() => clear({ direction: 'download', setState: setDown })}
        primary
      >
        <Icon name="trash alternate" />
        Clear All Completed Downloads
      </Button>
      <SavedAnalyses />
    </div>
  );
};

export default Data;
