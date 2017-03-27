using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web;

namespace FS.SpareStores.Helpers
{
    public class BlobAzureHelper
    {
        public const string ContainerImgBrand = "immagini-brand";
        public const string ContainerImgProdotti = "immagini-prodotti";
        public const string ContainerUploadDeldo = "upload-deldo";
        public const string ContainerUploadKijiji = "feed-kijiji";


        private CloudBlobClient ConnectionObject;
        private CloudBlobContainer SelectedContainer;
        private CloudBlockBlob SelectedBlob;
        private Boolean Started = false;

        public CloudBlobContainer GetCurrentContainer()
        {
            return SelectedContainer;
        }

        public Boolean IsInitialized
        {
            get { return Started; }
            set { Started = value; }
        }

        public BlobAzureHelper()
        {
            ConnectToAzureStorage(ConfigurationManager.AppSettings["StorageConnectionString"]);
        }
        #region Connessione
        //Connessione al server di Azure
        public Boolean ConnectToAzureStorage(string ConnectionString)
        {
            try
            {
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConnectionString);
                ConnectionObject = storageAccount.CreateCloudBlobClient();
                IsInitialized = true;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
        #endregion

        #region Liste
        //Prende la lista dei container
        public IEnumerable<CloudBlobContainer> GetListContainers()
        {
            return ConnectionObject.ListContainers();
        }

        //Prende la lista dei blobs dal container selezionato
        public IEnumerable<IListBlobItem> GetListBlobsOfCurrentContainer()
        {
            return SelectedContainer.ListBlobs(SelectedContainer.Name);
        }

        //Prende la lista dei blobs dal container passato come parametro
        public IEnumerable<IListBlobItem> GetListBlobsOfContainerByName(string myContainer)
        {
            return SelectedContainer.ListBlobs(myContainer);
        }
        #endregion

        #region Seleziona
        //Seleziona un container passandoli il nome
        public Boolean SelectContainerByName(string ContainerName)
        {
            SelectedContainer = ConnectionObject.GetContainerReference(ContainerName);
            return true;
        }

        //Seleziona il blob passandoli il nome
        public CloudBlockBlob SelectBlobByNameOfCurrentContainer(string filename)
        {
            return SelectedBlob = SelectedContainer.GetBlockBlobReference(filename);
        }
        #endregion



        //Ritorna l'url del blob corrente
        public string GetCurrentBlobUrl()
        {
            return SelectedBlob.Uri.ToString();
        }
        //Ritorna il nome del blob corrente
        public string GetCurrentBlobName()
        {
            return SelectedBlob.Name.ToString();
        }
        //Carica il file ricevuto tramite richiesta post

        public bool DeleteBlobFromCurrentContainer(string nameFile)
        {
            try
            {
                SelectedBlob = SelectedContainer.GetBlockBlobReference(nameFile);
                SelectedBlob.DeleteIfExists();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public CloudBlobContainer GetContaierByName(string name)
        {
            CloudBlobContainer retValue = ConnectionObject.GetContainerReference(name);

            return retValue;
        }

        public string CopyBlob(string fileName, string sourceContainerName, string destContainerName, bool deleteSource = false, string destFileName = null)
        {
            try
            {
                CloudBlobContainer sourceContainer = ConnectionObject.GetContainerReference(sourceContainerName);
                CloudBlobContainer destContainer = ConnectionObject.GetContainerReference(destContainerName);

                CloudBlockBlob sourceFile = sourceContainer.GetBlockBlobReference(fileName);
                CloudBlockBlob destFile;
                if (destFileName != null)
                    destFile = destContainer.GetBlockBlobReference(destFileName);
                else
                    destFile = sourceContainer.GetBlockBlobReference(fileName);

                destFile.StartCopy(sourceFile);

                if (deleteSource)
                {
                    sourceFile.DeleteIfExists();
                }

                return destFile.Uri.AbsoluteUri;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }


        //Carica come primo parametro il file ricevuto tramite codice (bisogna creare un MemoryStream partendo da un array di byte)
        //Ex. new MemoryStream(dataBytes)
        public Boolean UploadBlobToCurrentContainer(System.IO.Stream streamFile, string nameFile, string contentTypeFile)
        {
            try
            {
                SelectedBlob = SelectBlobByNameOfCurrentContainer(nameFile);
                SelectedBlob.Properties.ContentType = contentTypeFile;
                SelectedBlob.UploadFromStream(streamFile);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
        //Controlla se il blob selezionato esiste
        public Boolean CheckCurrentBlobExist()
        {
            return SelectedBlob.Exists();
        }
        //Controlla se il blob con il nome passato come parametro esiste
        public Boolean CheckBlobExist(string name)
        {
            return SelectBlobByNameOfCurrentContainer(name).Exists();
        }
        //Controlla se il blob con il nome passato come parametro esiste e lo cancella
        public Boolean CheckBlobExistAndDelete(string name)
        {
            CloudBlockBlob tempoBlob = SelectBlobByNameOfCurrentContainer(name);
            if (tempoBlob.Exists())
            {
                tempoBlob.Delete();
            }
            return true;
        }

        public void UploadStream(System.IO.Stream stream, string container, string fileName, string contentType)
        {
            this.SelectContainerByName(container);

            var byteArray = ((MemoryStream)stream).ToArray();

            //Carico 
            this.UploadBlobToCurrentContainer(new MemoryStream(byteArray), fileName, contentType);
        }
    }
}