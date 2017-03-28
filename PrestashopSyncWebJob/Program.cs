using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using FS.SpareStores.DAL.Model;
using Bukimedia.PrestaSharp.Factories;
using Bukimedia.PrestaSharp.Entities;
using System.Net;
using System.IO;
using FS.SpareStores.DAL.Helpers;
using System.Threading;

namespace PrestashopSyncWebJob
{
    // To learn more about Microsoft Azure WebJobs SDK, please see http://go.microsoft.com/fwlink/?LinkID=320976
    class Program
    {
        static string BaseUrl = string.Empty;
        static string Account = string.Empty;
        static string Password = string.Empty;

        static int idItaliano;

        static int idCaratteristicaBSW = 22; //Bordino
        static int idValCaratteristicaBSW_SI = 51857;
        static int idValCaratteristicaBSW_NO = 51858;

        static int idCaratteristicaXL = 23;
        static int idValCaratteristicaXL_SI = 51859;
        static int idValCaratteristicaXL_NO = 51860;

        static int idCaratteristicaVettura = 24;
        static int idValCaratteristicaVettura_Auto = 246722;
        static int idValCaratteristicaVettura_Furgone = 246723;

        public static void ClearAll()
        {
            //BaseUrl = "https://www.gommeusatestore.com/api/";
            //Account = "U4LZ2IUZK8RKC8SAMPRF5GMZIE2WGMWR";
            //Password = "";

            BaseUrl = "http://www.gommeusatestore.com/api/";
            Account = "R36UUTTXE3H6IMYPQVPJKA6PS17HZDMP";
            Password = "";

            Console.WriteLine("Eliminazione valori caratteristiche...");
            var povFactory = new ProductFeatureValueFactory(BaseUrl, Account, Password);
            foreach (var fv in povFactory.GetIds())
            {
                povFactory.Delete(fv);
            }

            //ManufacturerFactory mf = new ManufacturerFactory(BaseUrl, Account, Password);

            //foreach (var m in mf.GetIds())
            //{
            //    mf.Delete(m);
            //}

            Console.WriteLine("Eliminazione prodotti...");
            var prodFactory = new ProductFactory(BaseUrl, Account, Password);
            foreach (var p in prodFactory.GetIds())
            {
                prodFactory.Delete(p);
            }
        }

        public static void CheckOrdini()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                var db = new DBModel();

                foreach (var ps in db.PS_Shop.ToList())
                {
                    LogHelper.LogData(LogEntry.PrestashopWebJob, string.Format("Eliminazione prodotti non più presenti su gestionale da Prestashop {0}: {0}", ps.Descrizione));
                    BaseUrl = ps.URL;
                    Account = ps.AccessKEY;

                    var ordFactory = new OrderFactory(BaseUrl, Account, string.Empty);

                    foreach (var o in ordFactory.GetAll())
                    {
                        System.Console.WriteLine(
                            $"Ordine {o.reference} del {o.date_add}. Totale ordine con IVA: {o.total_paid}. Importo netto prodotti: {o.total_products}");

                        foreach (var r in o.associations.order_rows)
                        {
                            System.Console.WriteLine(
                                $"- Prodotto {r.product_id} (id {r.product_name}): importo {r.product_price}, quantità: {r.product_quantity}");
                        }

                        ordFactory.Delete(o.id.Value);
                    }

                    System.Console.ReadKey();


                }
            }
            catch (Exception ex)
            {
                LogHelper.LogData(LogEntry.PrestashopWebJob, "Errore aggiornamento immagine prodotto. " + ex);
            }
        }

        /// <summary>
        /// Elimina tutti i prodotti presenti su prestashop che non esistono più nel gestionale
        /// </summary>
        private static void EliminaProdottiInesistenti()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                var db = new DBModel();

                foreach (var ps in db.PS_Shop.Where(x => x.Disabled == false).ToList())
                {
                    LogHelper.LogData(LogEntry.PrestashopWebJob, string.Format("Eliminazione prodotti non più presenti su gestionale da Prestashop {0}: {0}", ps.Descrizione));
                    BaseUrl = ps.URL;
                    Account = ps.AccessKEY;

                    var prodFactory = new ProductFactory(BaseUrl, Account, Password);
                    foreach (var p in prodFactory.GetIds())
                    {
                        if (!db.PS_Product.Any(x => x.IdPrestashop == p))
                        {
                            var prod = prodFactory.Get(p);
                            LogHelper.LogData(LogEntry.PrestashopWebJob,
                                $"Eliminato dal magazzino il prodotto non più presente sul gestionale: {prod.name[0].Value}");
                            prodFactory.Delete(p);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogData(LogEntry.PrestashopWebJob, "Errore aggiornamento immagine prodotto. " + ex);
            }

        }

        private static void DisabilitaDispProdottoPrestashop(MagazzinoSyncShop shopSync, Disponibilita disp)
        {
            var prodFactory = new ProductFactory(BaseUrl, Account, Password);
            var psProd = disp.LstSyncPrestashop.FirstOrDefault(x => x.FK_ShopSync == shopSync.Id);
            product prod;

            if (psProd != null)
            {
                // modifica
                try
                {
                    prod = prodFactory.Get(psProd.IdPrestashop);
                    prod.active = 0;
                    prod.available_for_order = 0;
                }
                catch (Exception)
                {
                    // Prodotto eliminato da prestashop!!! :(
                }
            }
        }

        private static void SyncProdottiGestionale()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                var db = new DBModel();

                var helperCaratteristiche = new HelperCaratteristiche();

                foreach (var ps in db.PS_Shop.Where(x => x.Disabled == false).ToList())
                {
                    BaseUrl = ps.URL;
                    Account = ps.AccessKEY;

                    var prodFactory = new ProductFactory(BaseUrl, Account, Password);
                    var stockFactory = new StockAvailableFactory(BaseUrl, Account, Password);
                    var imgFactory = new ImageFactory(BaseUrl, Account, Password);

                    var lf = new LanguageFactory(BaseUrl, Account, Password);
                    var lstLingue = lf.GetIds();

                    foreach (var shopSync in ps.LstMagazziniSincronizzati.Where(x => x.SincronizzazioneAttiva).ToList())
                    {
                        var lstBrandDaSincronizzare = new List<int>();
                        var lstCaratteristicheDaSincronizzare = new List<int>();

                        if (shopSync.SincronizzazioneSelettiva)
                        {
                            LogHelper.LogData(LogEntry.PrestashopWebJob,
                                $"Sincronizzazione attivata per il magazzino: {shopSync.refMagazzino.Nome}. Carico le liste di marchi e caratteristiche da sincronizzare");
                            lstBrandDaSincronizzare = shopSync.LstSelectiveBrandSynch.Select(x => x.FK_Brand).ToList();
                            lstCaratteristicheDaSincronizzare = shopSync.LstSelectiveCarSync.Select(x => x.FK_ValoreCaratteristica).ToList();
                        }

                        idItaliano = shopSync.LstLingueMagazzino.FirstOrDefault(x => x.Italiano).IdPrestashop;
                        var totProdotti = shopSync.refMagazzino.LstDisponibilitaProdotti.Count();
                        var indexProdotto = 0;

                        if (totProdotti > 0)
                        {
                            LogHelper.LogData(LogEntry.PrestashopWebJob,
                                $"Sincronizzazione prodotti Prestashop {shopSync.refShop.Descrizione}");
                        }

                        foreach (var disp in shopSync.refMagazzino.LstDisponibilitaProdotti)
                        {
                            indexProdotto++;

                            Console.WriteLine($"{indexProdotto}/{totProdotti} idGestionale: {disp.Id}");
                            LogHelper.LogData(LogEntry.PrestashopWebJob,
                                $"{indexProdotto}/{totProdotti} idGestionale: {disp.Id}");

                            if (EscludiProdottoPerSincronizzazioneSelettiva(shopSync, lstBrandDaSincronizzare, disp, lstCaratteristicheDaSincronizzare)) continue;

                            PS_BrandSync manufacturer;

                            // Brand Prodotto
                            if (shopSync.LstMarchiSincronizzati.All(x => x.FK_Brand != disp.refPneumatico.FK_Brand))
                            {
                                // Devo sincronizzare il marchio
                                manufacturer = AddManufacturer(disp.refPneumatico.refBrand, shopSync.Id);
                                db.PS_BrandSync.Add(manufacturer);

                                try
                                {
                                    using (var webClient = new WebClient())
                                    {
                                        var imageBytes = webClient.DownloadData(disp.refPneumatico.refBrand.LogoURL);
                                        imgFactory.DeleteManufacturerImage(manufacturer.Id);
                                        imgFactory.AddManufacturerImage(manufacturer.Id, imageBytes);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.LogData(LogEntry.PrestashopWebJob,
                                        $"Errore impostazione logo produttore. Errore: {ex}");
                                    Console.WriteLine("Errore impostazione logo produttore.");
                                }
                            }
                            else
                            {
                                manufacturer = shopSync.LstMarchiSincronizzati.FirstOrDefault(x => x.FK_Brand == disp.refPneumatico.FK_Brand);
                            }

                            // Le categorie devono essere agganciate manualmente
                            var lstCategorieProdotto = shopSync.LstCategorieSincronizzate.Where(x => x.FK_Categoria == disp.refPneumatico.FK_Categoria).ToList();

                            if (lstCategorieProdotto.Count == 0)
                            {
                                LogHelper.LogData("LogEntry.PrestashopWebJob",
                                    $"Categoria non sincronizzata su prestashop: {disp.refPneumatico.refCategoria.Nome}");
                                Console.WriteLine($"Categoria non sincronizzata su prestashop: {disp.refPneumatico.refCategoria.Nome}");
                                continue;
                                //throw new Exception(
                                //    $"Nessuna categoria sincronizzata su Prestashop: {disp.refPneumatico.refCategoria.Nome}");
                            }

                            // Aggiungo come categoria anche la stagione (ghost)
                            lstCategorieProdotto.AddRange(shopSync.LstCategorieSincronizzate.Where(x => x.FK_Categoria == disp.refPneumatico.refStagione.FK_Categoria.Value));

                            // Feature Value Prestashop ID
                            PS_Feature altezza, diametro, indiceCarico, indiceVelocita, larghezza, mAndS, runflat, stagione, DOT, marca, modello, prezzo, condizione;
                            PS_FeatureValue altezzaVal, diametroVal, indiceCaricoVal, indiceVelocitaVal, larghezzaVal, mAndSVal, runflatVal, stagioneVal, DOTVal, marcaVal, modelloVal, battistradaUtileVal, prezzoVal, condizioneVal, battistradaPercFasciaVal;
                            PS_Feature battistradaUtile;
                            //PS_Feature battistradaPerc;


                            altezza = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Altezza && x.FK_ShopSync == shopSync.Id);
                            diametro = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Diametro && x.FK_ShopSync == shopSync.Id);
                            indiceCarico = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.IndiceCarico && x.FK_ShopSync == shopSync.Id);
                            indiceVelocita = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.IndiceVelocita && x.FK_ShopSync == shopSync.Id);
                            larghezza = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Larghezza && x.FK_ShopSync == shopSync.Id);
                            mAndS = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.MudAndSnow && x.FK_ShopSync == shopSync.Id);
                            stagione = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Stagione && x.FK_ShopSync == shopSync.Id);

                            // Caratteristiche con gestione particolare
                            runflat = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Runflat && x.FK_ShopSync == shopSync.Id);
                            DOT = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.DOT && x.FK_ShopSync == shopSync.Id);
                            marca = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Marca && x.FK_ShopSync == shopSync.Id);
                            modello = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Modello && x.FK_ShopSync == shopSync.Id);
                            battistradaUtile = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.BattistradaUtile && x.FK_ShopSync == shopSync.Id);
                            prezzo = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Prezzo && x.FK_ShopSync == shopSync.Id);
                            condizione = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Condizione && x.FK_ShopSync == shopSync.Id);
                            //battistradaPerc = db.PS_Feature.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.BattistradaFasciaPerc && x.FK_ShopSync == shopSync.Id);

                            // valori
                            altezzaVal = altezza.LstValues.FirstOrDefault(x => x.FK_Caratteristica == disp.refPneumatico.FK_Altezza);
                            diametroVal = diametro.LstValues.FirstOrDefault(x => x.FK_Caratteristica == disp.refPneumatico.FK_Diametro);
                            indiceCaricoVal = indiceCarico.LstValues.FirstOrDefault(x => x.FK_Caratteristica == disp.refPneumatico.FK_IndiceCarico);
                            indiceVelocitaVal = indiceVelocita.LstValues.FirstOrDefault(x => x.FK_Caratteristica == disp.refPneumatico.FK_IndiceVelocita);
                            larghezzaVal = larghezza.LstValues.FirstOrDefault(x => x.FK_Caratteristica == disp.refPneumatico.FK_Larghezza);
                            mAndSVal = mAndS.LstValues.FirstOrDefault(x => x.FK_Caratteristica == disp.refPneumatico.FK_MandS);
                            stagioneVal = stagione.LstValues.FirstOrDefault(x => x.FK_Caratteristica == disp.refPneumatico.FK_Stagione);

                            // Caratteristiche con gestione particolare
                            // - Runflat
                            var carRunFlat = disp.refPneumatico.Runflat ?
                                helperCaratteristiche.AggiungiValoreCaratteristicaProdotto(db, CaratteristicaProdotto.Runflat, "SI") :
                                helperCaratteristiche.AggiungiValoreCaratteristicaProdotto(db, CaratteristicaProdotto.Runflat, "NO");
                            runflatVal = runflat.LstValues.FirstOrDefault(x => x.FK_Caratteristica == carRunFlat.Id);

                            var strDOTValue = helperCaratteristiche.GetDotSting(disp);
                            var carDOT = helperCaratteristiche.AggiungiValoreCaratteristicaProdotto(db, CaratteristicaProdotto.DOT, strDOTValue);
                            carDOT = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.DOT && x.Valore == strDOTValue);
                            DOTVal = DOT.LstValues.FirstOrDefault(x => x.FK_Caratteristica == carDOT.Id);

                            var carMarca = helperCaratteristiche.AggiungiValoreCaratteristicaProdotto(db, CaratteristicaProdotto.Marca, disp.refPneumatico.refBrand.Nome);
                            marcaVal = marca.LstValues.FirstOrDefault(x => x.FK_Caratteristica == carMarca.Id);

                            var carModello = helperCaratteristiche.AggiungiValoreCaratteristicaProdotto(db, CaratteristicaProdotto.Modello, disp.refPneumatico.Modello);
                            modelloVal = modello.LstValues.FirstOrDefault(x => x.FK_Caratteristica == carModello.Id);

                            string strCondizione;
                            if (disp.refPneumatico.Usato)
                            {
                                strCondizione = "Usato";
                            }
                            else
                            {
                                strCondizione = "Nuovo";
                            }
                            var carCondizione = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Condizione && x.Valore == strCondizione);
                            if (carCondizione == null)
                            {
                                carCondizione = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Condizione, Valore = strCondizione };
                                db.CaratteristicheProdotti.Add(carCondizione);
                                db.SaveChanges();
                            }
                            condizioneVal = condizione.LstValues.FirstOrDefault(x => x.FK_Caratteristica == carCondizione.Id);

                            var prezzoRound = Convert.ToInt32(Math.Floor((double)disp.PrezzoIVA / 10) * 10);
                            var prezzoString = $"{prezzoRound}-{prezzoRound + 10} €";
                            var carPrezzo = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Prezzo && x.Valore == prezzoString);
                            if (carPrezzo == null)
                            {
                                carPrezzo = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Prezzo, Valore = prezzoString };
                                db.CaratteristicheProdotti.Add(carPrezzo);
                                db.SaveChanges();
                            }
                            prezzoVal = prezzo.LstValues.FirstOrDefault(x => x.FK_Caratteristica == carPrezzo.Id);

                            var battistradaResiduo = helperCaratteristiche.GetCaratteristicheBattistradaResiduo(db, disp);

                            battistradaUtileVal = battistradaUtile.LstValues.FirstOrDefault(x => x.FK_Caratteristica == battistradaResiduo.BattistradaUtile.Id);
                            if (battistradaUtileVal == null)
                            {
                                battistradaUtileVal = AddFeatureValue(battistradaUtile, battistradaResiduo.BattistradaUtile,
                                shopSync.Id);
                                db.PS_FeatureValue.Add(battistradaUtileVal);
                            }

                            //battistradaPercFasciaVal =
                            //    battistradaPerc.LstValues.FirstOrDefault(
                            //        x => x.FK_Caratteristica == battistradaResiduo.FasciaPercentualeResidua.Id);

                            if (altezzaVal == null)
                            {
                                altezzaVal = AddFeatureValue(altezza, disp.refPneumatico.refAltezza, shopSync.Id);
                                db.PS_FeatureValue.Add(altezzaVal);
                            }
                            if (diametroVal == null)
                            {
                                diametroVal = AddFeatureValue(diametro, disp.refPneumatico.refDiametro, shopSync.Id);
                                db.PS_FeatureValue.Add(diametroVal);
                            }
                            if (indiceCaricoVal == null)
                            {
                                indiceCaricoVal = AddFeatureValue(indiceCarico, disp.refPneumatico.refIndiceCarico, shopSync.Id);
                                db.PS_FeatureValue.Add(indiceCaricoVal);
                            }
                            if (indiceVelocitaVal == null)
                            {
                                indiceVelocitaVal = AddFeatureValue(indiceVelocita, disp.refPneumatico.refIndiceVelocita, shopSync.Id);
                                db.PS_FeatureValue.Add(indiceVelocitaVal);
                            }
                            if (larghezzaVal == null)
                            {
                                larghezzaVal = AddFeatureValue(larghezza, disp.refPneumatico.refLarghezza, shopSync.Id);
                                db.PS_FeatureValue.Add(larghezzaVal);
                            }
                            if (mAndSVal == null && disp.refPneumatico.refMudAndSnow != null)
                            {
                                mAndSVal = AddFeatureValue(mAndS, disp.refPneumatico.refMudAndSnow, shopSync.Id);
                                db.PS_FeatureValue.Add(mAndSVal);
                            }
                            if (stagioneVal == null)
                            {
                                stagioneVal = AddFeatureValue(stagione, disp.refPneumatico.refStagione, shopSync.Id);
                                db.PS_FeatureValue.Add(stagioneVal);
                            }
                            if (runflatVal == null)
                            {
                                runflatVal = AddFeatureValue(runflat, carRunFlat, shopSync.Id);
                                db.PS_FeatureValue.Add(runflatVal);
                            }
                            if (DOTVal == null)
                            {
                                DOTVal = AddFeatureValue(DOT, carDOT, shopSync.Id);
                                db.PS_FeatureValue.Add(DOTVal);
                            }
                            if (marcaVal == null)
                            {
                                marcaVal = AddFeatureValue(marca, carMarca, shopSync.Id);
                                db.PS_FeatureValue.Add(marcaVal);
                            }
                            if (modelloVal == null)
                            {
                                modelloVal = AddFeatureValue(modello, carModello, shopSync.Id);
                                db.PS_FeatureValue.Add(modelloVal);
                            }
                            if (condizioneVal == null)
                            {
                                condizioneVal = AddFeatureValue(condizione, carCondizione, shopSync.Id);
                                db.PS_FeatureValue.Add(condizioneVal);
                            }
                            if (prezzoVal == null)
                            {
                                prezzoVal = AddFeatureValue(prezzo, carPrezzo, shopSync.Id);
                                db.PS_FeatureValue.Add(prezzoVal);
                            }



                            var psProd = disp.LstSyncPrestashop.FirstOrDefault(x => x.FK_ShopSync == shopSync.Id);
                            product prod;

                            if (psProd != null)
                            {
                                // modifica
                                try
                                {
                                    prod = prodFactory.Get(psProd.IdPrestashop);

                                }
                                catch (Exception)
                                {
                                    // Prodotto eliminato da prestashop!!! :(
                                    continue;
                                }
                            }
                            else
                            {
                                if (disp.Quantita == 0 || disp.Prenotato)
                                    continue; // Se non ho quantità è inutile aggiungere
                                              // nuovo
                                prod = new product();
                            }

                            string gommeUsate;
                            //string metaDescription;
                            string descrizione;
                            string nomeProdotto;

                            if (disp.refPneumatico.Usato)
                            {
                                gommeUsate = $"Gomme usate - Batt. {disp.refPneumatico.PercResidua}%";

                                nomeProdotto =
                                    $"Gomme Usate - {disp.refPneumatico.refBrand.Nome} {disp.refPneumatico.Modello} - {disp.refPneumatico.refLarghezza.Valore} {disp.refPneumatico.refAltezza.Valore} R{disp.refPneumatico.refDiametro.Valore} {disp.refPneumatico.refIndiceCarico.Valore}{disp.refPneumatico.refIndiceVelocita.Valore}";

                                descrizione = string.Format("<strong>Gomme Usate {0} {1} {2} {3} R{4} {5} {6}</strong>. Acquista le tue <strong>{0} {1} {2} {3} R{4} {5} {6}</strong> su GOMMEUSATEstore.com: <strong>gomme usate</strong> ai migliori prezzi. <strong>Pneumatici usati</strong> testati e garantiti.",
                                    disp.refPneumatico.refBrand.Nome,
                                    disp.refPneumatico.Modello,
                                    disp.refPneumatico.refLarghezza.Valore,
                                    disp.refPneumatico.refAltezza.Valore,
                                    disp.refPneumatico.refDiametro.Valore,
                                    disp.refPneumatico.refIndiceCarico.Valore,
                                    disp.refPneumatico.refIndiceVelocita.Valore);

                                // categorie pneumatici usati
                                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 1 });
                                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 2 });
                                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 43 });
                            }
                            else
                            {
                                gommeUsate = "Pneumatici";

                                nomeProdotto =
                                    $"Pneumatici - {disp.refPneumatico.refBrand.Nome} {disp.refPneumatico.Modello} - {disp.refPneumatico.refLarghezza.Valore} {disp.refPneumatico.refAltezza.Valore} R{disp.refPneumatico.refDiametro.Valore} {disp.refPneumatico.refIndiceCarico.Valore}{disp.refPneumatico.refIndiceVelocita.Valore}";

                                descrizione = string.Format("<strong>Pneumatici {0} {1} {2} {3} R{4} {5} {6}</strong>. Acquista i tuoi <strong>{0} {1} {2} {3} R{4} {5} {6}</strong> su GOMMEUSATEstore.com: : <strong>pneumatici nuovi ed usati</strong> ai migliori prezzi. <strong>Gomme auto</strong> delle migliori marche con garanzia alta qualità.",
                                    disp.refPneumatico.refBrand.Nome,
                                    disp.refPneumatico.Modello,
                                    disp.refPneumatico.refLarghezza.Valore,
                                    disp.refPneumatico.refAltezza.Valore,
                                    disp.refPneumatico.refDiametro.Valore,
                                    disp.refPneumatico.refIndiceCarico.Valore,
                                    disp.refPneumatico.refIndiceVelocita.Valore);

                                // categorie pneumatici nuovi
                                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 1 });
                                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 2 });
                                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 51 });
                            }


                            var linkRewrite = string.Format("{4} {0} {1}-{2}R{3}", disp.refPneumatico.refBrand.Nome, disp.refPneumatico.refLarghezza.Valore, disp.refPneumatico.refAltezza.Valore,
                                disp.refPneumatico.refDiametro.Valore, gommeUsate);

                            linkRewrite = linkRewrite.ToLower();
                            linkRewrite = System.Text.RegularExpressions.Regex.Replace(linkRewrite, @"[^a-z0-9\s-]", ""); // Remove all non valid chars          
                            linkRewrite = System.Text.RegularExpressions.Regex.Replace(linkRewrite, @"\s+", " ").Trim(); // convert multiple spaces into one space  
                            linkRewrite = System.Text.RegularExpressions.Regex.Replace(linkRewrite, @"\s", "-"); // //Replace spaces by dashes

                            if (prod.name.Count > 0)
                            {
                                for (var i = 0; i < prod.name.Count; i++)
                                {
                                    prod.name[i].Value = nomeProdotto;
                                }
                            }
                            else
                            {
                                foreach (var l in lstLingue)
                                {
                                    prod.AddName(new Bukimedia.PrestaSharp.Entities.AuxEntities.language() { id = l, Value = nomeProdotto });
                                }
                            }

                            if (prod.link_rewrite.Count > 0)
                            {
                                for (var i = 0; i < prod.link_rewrite.Count; i++)
                                {
                                    prod.link_rewrite[i].Value = linkRewrite;
                                }
                            }
                            else
                            {
                                foreach (var l in lstLingue.OrderBy(x => x))
                                {
                                    prod.AddLinkRewrite(new Bukimedia.PrestaSharp.Entities.AuxEntities.language() { id = l, Value = linkRewrite });
                                }
                            }

                            if (prod.description_short.Count > 0)
                            {
                                for (var i = 0; i < prod.description_short.Count; i++)
                                {
                                    prod.description_short[i].Value = string.Empty;
                                }
                            }

                            if (prod.description.Count > 0)
                            {
                                for (var i = 0; i < prod.description.Count; i++)
                                {
                                    prod.description[i].Value = $"{descrizione}";
                                }
                            }
                            else
                            {
                                foreach (var l in lstLingue.OrderBy(x => x))
                                {
                                    prod.description.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.language()
                                    {
                                        id = l,
                                        Value = descrizione
                                    });
                                }
                            }
                            prod.price = Convert.ToDecimal(Math.Ceiling(disp.PrezzoIVA * (1 + shopSync.MarkupPercentuale / 100) + shopSync.MarkupValore) - 0.01);
                            prod.show_price = 1;
                            if (!string.IsNullOrWhiteSpace(disp.refPneumatico.EAN13))
                                prod.ean13 = disp.refPneumatico.EAN13;

                            prod.associations.product_features.Clear();
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = altezza.IdPrestashop, id_feature_value = altezzaVal.IdPrestashop });
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = diametro.IdPrestashop, id_feature_value = diametroVal.IdPrestashop });
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = indiceCarico.IdPrestashop, id_feature_value = indiceCaricoVal.IdPrestashop });
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = indiceVelocita.IdPrestashop, id_feature_value = indiceVelocitaVal.IdPrestashop });
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = larghezza.IdPrestashop, id_feature_value = larghezzaVal.IdPrestashop });
                            if (mAndSVal != null)
                            {
                                prod.associations.product_features
                                    .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = mAndS.IdPrestashop, id_feature_value = mAndSVal.IdPrestashop });
                            }

                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = stagione.IdPrestashop, id_feature_value = stagioneVal.IdPrestashop });
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = runflat.IdPrestashop, id_feature_value = runflatVal.IdPrestashop });
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = DOT.IdPrestashop, id_feature_value = DOTVal.IdPrestashop });
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = marca.IdPrestashop, id_feature_value = marcaVal.IdPrestashop });
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = modello.IdPrestashop, id_feature_value = modelloVal.IdPrestashop });
                            prod.associations.product_features
                                    .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = condizione.IdPrestashop, id_feature_value = condizioneVal.IdPrestashop });
                            prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = prezzo.IdPrestashop, id_feature_value = prezzoVal.IdPrestashop });
                            if (battistradaResiduo != null)
                            {
                                // battistradaUtile
                                prod.associations.product_features
                                .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = battistradaUtile.IdPrestashop, id_feature_value = battistradaUtileVal.IdPrestashop });

                                // Fascia utilizzo
                                //prod.associations.product_features
                                //.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = battistradaPerc.IdPrestashop, id_feature_value = battistradaPercFasciaVal.IdPrestashop });
                            }

                            // Bordino
                            if (disp.refPneumatico.Bordino)
                            {
                                prod.associations.product_features
                                    .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = idCaratteristicaBSW, id_feature_value = idValCaratteristicaBSW_SI });
                            }
                            else
                            {
                                prod.associations.product_features
                                    .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = idCaratteristicaBSW, id_feature_value = idValCaratteristicaBSW_NO });
                            }

                            // XL
                            if (disp.refPneumatico.XL)
                            {
                                prod.associations.product_features
                                    .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = idCaratteristicaXL, id_feature_value = idValCaratteristicaXL_SI });
                            }
                            else
                            {
                                prod.associations.product_features
                                    .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = idCaratteristicaXL, id_feature_value = idValCaratteristicaXL_NO });
                            }

                            if (disp.refPneumatico.refCategoria.Nome == "AUTO")
                            {
                                prod.associations.product_features
                                    .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = idCaratteristicaVettura, id_feature_value = idValCaratteristicaVettura_Auto });
                            }

                            if (disp.refPneumatico.refCategoria.Nome == "FURGONE")
                            {
                                prod.associations.product_features
                                    .Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.product_feature() { id = idCaratteristicaVettura, id_feature_value = idValCaratteristicaVettura_Furgone });
                            }

                            prod.associations.categories.Clear();
                            // Categoria in evidenza
                            if (disp.InEvidenzaPrestashop)
                            {
                                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 50 });
                            }

                            foreach (var psCat in disp.refPneumatico.refCategoria.LstSyncPrestashop.Where(x => x.FK_ShopSync == shopSync.Id))
                            {
                                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = psCat.IdPrestashop });
                                prod.id_category_default = psCat.IdPrestashop;
                            }
                            // Associo categoria fake per stagione
                            var catStagione = db.CategorieProdotti.Find(disp.refPneumatico.refStagione.FK_Categoria);
                            var stagPrestashop = catStagione.LstSyncPrestashop.FirstOrDefault(x => x.FK_ShopSync == shopSync.Id);
                            if (stagPrestashop != null)
                            {
                                if (prod.associations.categories.All(x => x.id != stagPrestashop.IdPrestashop))
                                {
                                    prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = stagPrestashop.IdPrestashop });
                                }
                            }

                            prod.id_manufacturer = (long)disp.refPneumatico.refBrand.LstSyncPrestashop.FirstOrDefault(x => x.FK_ShopSync == shopSync.Id).IdPrestashop;

                            if (disp.Quantita - disp.LstVendita.Sum(x => x.Quantita) > 0 && !disp.Prenotato)
                            {
                                prod.active = 1;
                                prod.available_for_order = 1;
                            }
                            else
                            {
                                prod.active = 0;
                                prod.available_for_order = 0;
                            }

                            prod.condition = disp.refPneumatico.Usato ? "used" : "new";

                            if (psProd != null)
                            {
                                prodFactory.Update(prod);
                                psProd.DataAggiornamento = DateTime.Now;
                            }
                            else
                            {
                                prod = prodFactory.Add(prod);

                                // Link rewrite reimpostato
                                prod.link_rewrite[0].Value = linkRewrite;

                                prodFactory.Update(prod);

                                if (prod.id.HasValue)
                                {
                                    var product = new PS_Product()
                                    {
                                        DataAggiornamento = DateTime.Now,
                                        FK_Disponibilita = disp.Id,
                                        IdPrestashop = Convert.ToInt32(prod.id.Value),
                                        FK_ShopSync = shopSync.Id
                                    };
                                    db.PS_Product.Add(product);

                                    try
                                    {
                                        foreach (var img in disp.refPneumatico.LstImmagini)
                                        {
                                            var imageData = ImagesHelper.GetImageResized(img.URLImmagine);

                                            if (imageData != null)
                                            {
                                                imgFactory.AddProductImage(prod.id.Value, imageData);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogHelper.LogData(LogEntry.PrestashopWebJob, "Errore aggiornamento immagine prodotto. " + ex);
                                        Console.WriteLine("Errore aggiornamento immagine prodotto");
                                    }
                                }
                            }

                            var filter = new Dictionary<string, string>();
                            filter.Add("id_product", prod.id.ToString());
                            var saList = stockFactory.GetByFilter(filter, null, null);
                            var sa = saList.FirstOrDefault();

                            if (sa != null && sa.quantity != disp.Quantita)
                            {
                                if (disp.Prenotato)
                                {
                                    sa.quantity = 0;
                                }
                                else
                                {
                                    sa.quantity = disp.Quantita - disp.LstVendita.Sum(x => x.Quantita);
                                    if (sa.quantity < 0)
                                        sa.quantity = 0;
                                    stockFactory.Update(sa);
                                }
                            }

                            db.SaveChanges();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Errore sincronizzazione Prestashop. " + ex.ToString());
                LogHelper.LogData(LogEntry.PrestashopWebJob, "Errore sincronizzazione Prestashop. " + ex.ToString());
            }

            Console.WriteLine("### SINCRONIZZAZIONE COMPLETATA ###");
            LogHelper.LogData(LogEntry.PrestashopWebJob, "### SINCRONIZZAZIONE COMPLETATA ###");
        }

        private static void SetCaratteristicheCustom(Disponibilita disp, product prod)
        {
            if (disp.refPneumatico.refLarghezza.Valore == "205" &&
                    disp.refPneumatico.refAltezza.Valore == "55" &&
                    disp.refPneumatico.refDiametro.Valore == "16")
            {
                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 57 });
            }

            if (disp.refPneumatico.refLarghezza.Valore == "174" &&
                    disp.refPneumatico.refAltezza.Valore == "65" &&
                    disp.refPneumatico.refDiametro.Valore == "14")
            {
                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 58 });
            }

            if (disp.refPneumatico.refLarghezza.Valore == "185" &&
                    disp.refPneumatico.refAltezza.Valore == "65" &&
                    disp.refPneumatico.refDiametro.Valore == "15")
            {
                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 59 });
            }

            if (disp.refPneumatico.refLarghezza.Valore == "185" &&
                    disp.refPneumatico.refAltezza.Valore == "55" &&
                    disp.refPneumatico.refDiametro.Valore == "15")
            {
                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 60 });
            }

            if (disp.refPneumatico.refLarghezza.Valore == "225" &&
                    disp.refPneumatico.refAltezza.Valore == "45" &&
                    disp.refPneumatico.refDiametro.Valore == "17")
            {
                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 61 });
            }

            if (disp.refPneumatico.refLarghezza.Valore == "215" &&
                    disp.refPneumatico.refAltezza.Valore == "55" &&
                    disp.refPneumatico.refDiametro.Valore == "17")
            {
                prod.associations.categories.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.category() { id = 61 });
            }
        }

        private static bool EscludiProdottoPerSincronizzazioneSelettiva(MagazzinoSyncShop shopSync, List<int> lstBrandDaSincronizzare,
            Disponibilita disp, List<int> lstCaratteristicheDaSincronizzare)
        {
            if (shopSync.SincronizzazioneSelettiva)
            {
                if (!lstBrandDaSincronizzare.Contains(disp.refPneumatico.FK_Brand))
                {
                    LogHelper.LogData(LogEntry.PrestashopWebJob,
                        $"Sincronizzazione selettiva attivata: prodotto {disp.Id} escluso per marchio non da sincronizzare.");

                    // Azzero disponibilità se prodotto esistente
                    DisabilitaDispProdottoPrestashop(shopSync, disp);
                    return true;
                }

                if (!lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_Altezza) ||
                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_Diametro) ||
                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_IndiceCarico) ||
                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_IndiceVelocita) ||
                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_Larghezza) ||
                    (disp.refPneumatico.FK_MandS.HasValue &&
                     !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_MandS.Value)) ||
                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_Stagione))
                {
                    LogHelper.LogData(LogEntry.PrestashopWebJob,
                        $"Sincronizzazione selettiva attivata: prodotto {disp.Id} escluso per caratteristica non da sincronizzare.");
                    DisabilitaDispProdottoPrestashop(shopSync, disp);
                    return true;
                }
            }
            return false;
        }

        // Please set the following connection strings in app.config for this WebJob to run:
        // AzureWebJobsDashboard and AzureWebJobsStorage
        static void Main()
        {
            try
            {
                // All'inizio di ogni esecuzione elimino i log della precedente
                LogHelper.ClearLog(LogEntry.PrestashopWebJob);
                LogHelper.SetLogStatus(LogEntry.PrestashopWebJob, false, true, "Sincronizzazione in corso");

                //ClearAll();
                EliminaProdottiInesistenti();
                SyncProdottiGestionale();

                //CheckOrdini();
            }
            catch (Exception ex)
            {
                LogHelper.SetLogStatus(LogEntry.PrestashopWebJob, false, false, string.Format("Errore nella sincronizzazione: {0}", ex.ToString()));
                LogHelper.LogData(LogEntry.PrestashopWebJob, string.Format("Errore nella sincronizzazione: {0}", ex.ToString()));
            }

            LogHelper.SetLogStatus(LogEntry.PrestashopWebJob, true, false, "Sincronizzazione completata");
            Thread.Sleep(new TimeSpan(0, 20, 0));
        }

        private static PS_FeatureValue AddFeatureValue(PS_Feature caratteristica, CaratteristicaProdotto valore, int idShopSync)
        {
            var povFactory = new ProductFeatureValueFactory(BaseUrl, Account, Password);

            var ov = new product_feature_value { id_feature = (long)caratteristica.IdPrestashop };
            ov.value.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.language() { id = idItaliano, Value = !string.IsNullOrWhiteSpace(valore.Valore) ? valore.Valore : "n.s." });

            ov = povFactory.Add(ov);

            if (ov.id.HasValue)
            {
                var featuraVal = new PS_FeatureValue() { DataAggiornamento = DateTime.Now, FK_Caratteristica = valore.Id, FK_Feature = caratteristica.Id, IdPrestashop = Convert.ToInt32(ov.id) };
                return featuraVal;
            }

            throw new Exception("Errore aggiunta valore caratteristica");
        }

        static PS_BrandSync AddManufacturer(Brand brand, int idShopSync)
        {
            var mf = new ManufacturerFactory(BaseUrl, Account, Password);

            Console.WriteLine($"Aggiunta produttore: {brand.Nome}");

            var man = new manufacturer();
            man.name = brand.Nome;
            man.date_add = ConvertiData(DateTime.Now);
            man.date_upd = ConvertiData(DateTime.Now);
            man.description.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.language() { id = idItaliano, Value = brand.Nome });
            man.meta_title.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.language() { id = idItaliano, Value = brand.Nome });
            man.meta_keywords.Add(new Bukimedia.PrestaSharp.Entities.AuxEntities.language() { id = idItaliano, Value = brand.Nome });
            man.active = 1;

            man = mf.Add(man);


            if (man.id.HasValue)
            {
                var brandSync = new PS_BrandSync() { DataAggiornamento = DateTime.Now, FK_Brand = brand.Id, FK_ShopSync = idShopSync };
                brandSync.IdPrestashop = Convert.ToInt32(man.id);
                return brandSync;
            }

            throw new Exception("Errore aggiunta marchio");
        }


        #region Utility

        private static string ConvertiData(DateTime data)
        {
            return
                $"{data.Year.ToString().PadLeft(2, '0')}-{data.Month.ToString().PadLeft(2, '0')}-{data.Day.ToString().PadLeft(2, '0')} {data.Hour.ToString().PadLeft(2, '0')}:{data.Minute.ToString().PadLeft(2, '0')}:{data.Second.ToString().PadLeft(2, '0')}";
        }

        #endregion
    }
}
