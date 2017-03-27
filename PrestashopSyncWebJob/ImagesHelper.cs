using ImageProcessor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PrestashopSyncWebJob
{
    public static class ImagesHelper
    {
        public static byte[] GetImageResized(string imageURL, int byteSize = 2145728)
        {
            ImageFactory imageFactory = new ImageFactory();
            int quality = 70;
            Size size = new Size(800, 0);

            byte[] retImage = null;

            using (var webClient = new WebClient())
            {
                byte[] imageBytes = webClient.DownloadData(imageURL);

                MemoryStream inStream = new MemoryStream(imageBytes);
                MemoryStream outStream = new MemoryStream();

                imageFactory.Load(inStream)
                        .Resize(size)
                        .Quality(quality)
                        .AutoRotate()
                        .Save(outStream);


                retImage = outStream.ToArray();

                //if (imageBytes.LongCount() < byteSize)
                //{
                //    retImage = imageBytes;
                //}
                //else
                //{

                //}

            }

            return retImage;
        }
    }
}
