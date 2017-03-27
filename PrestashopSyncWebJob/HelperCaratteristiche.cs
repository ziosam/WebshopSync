using FS.SpareStores.DAL.Model;
using System;
using System.Linq;

namespace PrestashopSyncWebJob
{
    public class HelperCaratteristiche
    {
        public CaratteristicaProdotto AggiungiValoreCaratteristicaProdotto(DBModel db, string tipoCaratteristica, string valore)
        {
            var valoreCaratteristica = db.CaratteristicheProdotti.FirstOrDefault(
                x => x.TipoCaratteristica == tipoCaratteristica && 
                x.Valore == valore);

            if (valoreCaratteristica != null) return valoreCaratteristica;
            valoreCaratteristica = new CaratteristicaProdotto
            {
                TipoCaratteristica = tipoCaratteristica,
                Valore = valore
            };
            db.CaratteristicheProdotti.Add(valoreCaratteristica);
            db.SaveChanges();

            return valoreCaratteristica;
        }

        public string GetDotSting(Disponibilita disp)
        {
            string strDotValue;
            if (string.IsNullOrEmpty(disp.refPneumatico.DOT))
            {
                disp.refPneumatico.DOT = string.Empty;
                strDotValue = "-";
            }
            else
            {
                disp.refPneumatico.DOT = disp.refPneumatico.DOT.PadLeft(4, '0');
                strDotValue =
                    $"Settimana {disp.refPneumatico.DOT.Substring(0, 2)} Anno 20{disp.refPneumatico.DOT.Substring(2, 2)}";
            }

            return strDotValue;
        }

        public HelperBattistradaResiduo GetCaratteristicheBattistradaResiduo(DBModel db, Disponibilita disp)
        {
            string percResiduaVal;
            string battistradaUtileString;

            if (disp.refPneumatico.Usato && disp.refPneumatico.PercResidua.HasValue)
            {
                // Fascia percentuale residua
                var percResidua = Convert.ToInt32(Math.Floor((double) disp.refPneumatico.PercResidua.Value / 10) * 10);
                percResiduaVal = $"{percResidua}-{percResidua + 10}%";
                battistradaUtileString = disp.refPneumatico.PercResidua.Value + "%";
            }
            else
            {
                battistradaUtileString = "NUOVO";
                percResiduaVal = "NUOVO";
            }

            var carBattistradaUtile = AggiungiValoreCaratteristicaProdotto(db,
                   CaratteristicaProdotto.BattistradaUtile, battistradaUtileString);

            var carFasciaPercentualeResidua = AggiungiValoreCaratteristicaProdotto(db,
                CaratteristicaProdotto.BattistradaFasciaPerc, percResiduaVal);

            var retValue = new HelperBattistradaResiduo
            {
                FasciaPercentualeResidua = carFasciaPercentualeResidua,
                BattistradaUtile = carBattistradaUtile
            };

            return retValue;
        }
    }

    public class HelperBattistradaResiduo
    {
        public CaratteristicaProdotto BattistradaUtile { get; set; }
        public CaratteristicaProdotto FasciaPercentualeResidua { get; set; }
    }

}
