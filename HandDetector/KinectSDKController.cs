﻿// author：      Administrator
// created time：2014/1/14 15:59:58
// organizatioin:CURE lab, CUHK
// copyright：   2014-2015
// CLR：         4.0.30319.18052
// project link：https://github.com/huangfuyang/Sign-Language-with-Kinect

using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

using CURELab.SignLanguage.StaticTools;
using Emgu.CV;
using Emgu.CV.Structure;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;

using CURELab.SignLanguage.HandDetector;

namespace CURELab.SignLanguage.HandDetector
{
    /// <summary>
    /// Kinect SDK controller
    /// </summary>
    public class KinectSDKController : KinectController
    {

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor sensor;


        /// <summary>
        /// Intermediate storage for the depth data received from the camera
        /// </summary>
        private DepthImagePixel[] depthPixels;

        /// <summary>
        /// Intermediate storage for the color data received from the camera
        /// </summary>
        private byte[] colorPixels;

        private Skeleton[] skeletons;

        private System.Drawing.Point rightHandPosition;
        private System.Drawing.Point headPosition;

        public static double CullingThresh = 5;
        public static double AngleRotate = Math.PI / 6;
        public static float AngleRotateTan = (float)Math.Tan(AngleRotate);
        private KinectSDKController()
            : base()
        {
            KinectSensor.KinectSensors.StatusChanged += Kinect_StatusChanged;
        }

        public static KinectController GetSingletonInstance()
        {
            if (singleInstance == null)
            {
                singleInstance = new KinectSDKController();
            }
            return singleInstance;
        }

        public override void Initialize(string uri = null)
        {
            // Look through all sensors and start the first connected one.
            // This requires that a Kinect is connected at the time of app startup.
            // To make your app robust against plug/unplug, 
            // it is recommended to use KinectSensorChooser provided in Microsoft.Kinect.Toolkit (See components in Toolkit Browser).
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (null != this.sensor)
            {
                // Turn on the color stream to receive color frames
                this.sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                this.sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                this.sensor.SkeletonStream.Enable();
                // Allocate space to put the pixels we'll receive           
                this.colorPixels = new byte[this.sensor.ColorStream.FramePixelDataLength];
                // Allocate space to put the depth pixels we'll receive
                this.depthPixels = new DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];

                // This is the bitmap we'll display on-screen
                this.ColorWriteBitmap = new WriteableBitmap(this.sensor.ColorStream.FrameWidth, this.sensor.ColorStream.FrameHeight, 96.0, 96.0, System.Windows.Media.PixelFormats.Bgr32, null);
                this.DepthWriteBitmap = new WriteableBitmap(this.sensor.DepthStream.FrameWidth, this.sensor.DepthStream.FrameHeight, 96.0, 96.0, System.Windows.Media.PixelFormats.Bgr32, null);
                this.GrayWriteBitmap = new WriteableBitmap(512, 200, 96.0, 96.0, System.Windows.Media.PixelFormats.Gray8, null);


                // Add an event handler to be called whenever there is new frame data
                this.sensor.AllFramesReady += this.AllFrameReady;
                this.Status = Properties.Resources.Connected;

            }

            rightHandPosition = new System.Drawing.Point();
            //ConsoleManager.Show();
            if (null == this.sensor)
            {
                this.Status = Properties.Resources.NoKinectReady;
            }

        }


        private void Kinect_StatusChanged(object sender, StatusChangedEventArgs e)
        {
            switch (e.Status)
            {
                case KinectStatus.Connected:
                    if (sensor == null)
                    {
                        sensor = e.Sensor;
                        Initialize();
                        Start();
                    }
                    break;
                case KinectStatus.Disconnected:
                    if (sensor == e.Sensor)
                    {

                        this.Status = Properties.Resources.NoKinectReady;
                        // Notify user, change state of APP appropriately
                    }
                    break;
                case KinectStatus.NotReady:
                    break;
                case KinectStatus.NotPowered:
                    if (sensor == e.Sensor)
                    {
                        this.Status = Properties.Resources.NoKinectReady;
                        // Notify user, change state of APP appropriately
                    }
                    break;
                default:
                    // Throw exception, notify user or ignore depending on use case
                    break;
            }
        }

        byte headDepth = 0;
        private void AllFrameReady(object sender, AllFramesReadyEventArgs e)
        {
            using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
            {
                if (colorFrame != null)
                {
                    // Copy the pixel data from the image to a temporary array
                    colorFrame.CopyPixelDataTo(this.colorPixels);

                    // Write the pixel data into our bitmap
                    this.ColorWriteBitmap.WritePixels(
                        new System.Windows.Int32Rect(0, 0, this.ColorWriteBitmap.PixelWidth, this.ColorWriteBitmap.PixelHeight),
                        this.colorPixels,
                        this.ColorWriteBitmap.PixelWidth * sizeof(int),
                        0);
                }
            }

            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (depthFrame != null)
                {
                    // Copy the pixel data from the image to a temporary array
                    depthFrame.CopyDepthImagePixelDataTo(this.depthPixels);
                    short[] depthData = new short[depthFrame.PixelDataLength];
                    // Get the min and max reliable depth for the current frame
                    int minDepth = depthFrame.MinDepth;
                    int maxDepth = depthFrame.MaxDepth;
                    int width = depthFrame.Width;
                    int height = depthFrame.Height;
                    // Convert the depth to RGB
                    int colorPixelIndex = 0;
                    for (int i = 0; i < this.depthPixels.Length; ++i)
                    {
                        // Get the depth for this pixel
                        short depth = depthPixels[i].Depth;
                        // transform depth map
                        TransformDepth(ref depth, i / 640, AngleRotateTan);
                        depthData[i] = depth;
                        // To convert to a byte, we're discarding the most-significant
                        // rather than least-significant bits.
                        // We're preserving detail, although the intensity will "wrap."
                        // Values outside the reliable depth range are mapped to 0 (black).

                        // Note: Using conditionals in this loop could degrade performance.
                        // Consider using a lookup table instead when writing production code.
                        // See the KinectDepthViewer class used by the KinectExplorer sample
                        // for a lookup table example.
                        int intensity = (depth >= minDepth && depth <= maxDepth ? depth : minDepth);
                        intensity = (int)((float)(intensity - minDepth) / (maxDepth - minDepth) * 255);
                        byte density = (byte)intensity;
                        // Write out blue byte
                        this.colorPixels[colorPixelIndex++] = density;

                        // Write out green byte
                        this.colorPixels[colorPixelIndex++] = density;

                        // Write out red byte                        
                        this.colorPixels[colorPixelIndex++] = density;

                        // We're outputting BGR, the last byte in the 32 bits is unused so skip it
                        // If we were outputting BGRA, we would write alpha here.
                        ++colorPixelIndex;

                    }
                 
                    BitmapSource depthBS = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, colorPixels, width * 4);
                    Bitmap depthBMP = depthBS.ToBitmap();

                    //erase background
                    double temp1 = 0.2 * Convert.ToDouble(colorPixels[headPosition.X * 4 + headPosition.Y * 640 * 4]);
                    double temp2 = 0.8 * Convert.ToDouble(headDepth);
                    if (headDepth == 0)
                    {
                        headDepth = colorPixels[headPosition.X * 4 + headPosition.Y * 640 * 4];
                    }
                    else
                    {
                        headDepth = (byte)(temp1 + temp2);
                    }
                    headDepth = colorPixels[headPosition.X * 4 + headPosition.Y * 640 * 4];
                    //Console.WriteLine(headDepth);
                    
                    EraseBackground(depthBMP, (byte)(headDepth - (byte)CullingThresh));
                    
                    ////region growing
                    //PointF startPoint = new PointF(rightHandPosition.X, rightHandPosition.Y);
                    //RegionGrow(startPoint, depthData, depthBMP);
                    ////edge detection
                    //Bitmap edgeBmp = OpenCVController.GetSingletonInstance().RecogEdgeBgra(depthBMP).ToBitmap();
                    //Rectangle[] rects = OpenCVController.GetSingletonInstance().RecogBlob(depthBMP);
                    //if (rects != null)
                    //{
                    //    RecognizeAndDrawRects(depthBMP, rects);
                    //}

                    //find hand
                    Image<Bgra, byte> depthImg = new Image<Bgra, byte>(depthBMP);
                    FindHandPart(ref depthImg);

                    //draw gray histogram
                    //Bitmap histo = m_OpenCVController.Histogram(depthBMP);
                    //UpdateImage(GrayWriteBitmap, histo);

                    //draw hand position from kinect
                    //DrawHandPosition(depthBMP, rightHandPosition, System.Drawing.Brushes.Purple);
                    //upadte UI
                    ImageConverter.UpdateWriteBMP(DepthWriteBitmap, depthImg.ToBitmap());

                }
            }
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    skeletonFrame.CopySkeletonDataTo(this.skeletons);
                    Skeleton skel = skeletons[0];
                    if (skel.TrackingState == SkeletonTrackingState.Tracked)
                    {
                        SkeletonPoint rightHand = skeletons[0].Joints[JointType.HandRight].Position;
                        SkeletonPoint head = skeletons[0].Joints[JointType.Head].Position;
                        rightHandPosition = SkeletonPointToScreen(rightHand);
                        headPosition = SkeletonPointToScreen(head);

                    }
                }
            }
        }

        private void RecognizeAndDrawRects(Bitmap bmp, Rectangle[] rects)
        {
            var LinqRects =
                from rect in rects
                where rect.GetYCenter() > 50
                orderby rect.GetRectArea() descending
                select rect;
            rects = LinqRects.ToArray();
            Rectangle rightHand;
            Rectangle leftHand;
            Font textFont = new Font(FontFamily.Families[0], 20);
            int textOffset = 40;
            if (rects.Count() >= 2)//two hands
            {
                if (rects[0].GetXCenter() > rects[1].GetXCenter())
                {
                    rightHand = rects[0];
                    leftHand = rects[1];
                }
                else
                {
                    rightHand = rects[1];
                    leftHand = rects[0];
                }
                if (rightHand.IntersectsWith(leftHand))
                {
                    Intersect = true;
                }
                else
                {
                    Intersect = false;
                }
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.DrawRectangle(new Pen(Brushes.Red, 2), rightHand);
                    g.DrawString("right", textFont, Brushes.Red,
                        (float)rightHand.X, (float)rightHand.Y - textOffset);
                    g.DrawRectangle(new Pen(Brushes.Red, 2), leftHand);
                    g.DrawString("left", textFont, Brushes.Red,
                        (float)leftHand.X, (float)leftHand.Y - textOffset);
                    g.Save();
                }

            }
            else if (rects.Count() == 1) // one rectangle
            {
                string text = "";
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    if (Intersect)
                    {
                        text = "Two hands";
                    }
                    else
                    {
                        text = "right";
                    }
                    g.DrawRectangle(new Pen(Brushes.Red, 2), rects[0]);
                    g.DrawString(text, textFont, Brushes.Red,
                        (float)rects[0].X, (float)rects[0].Y - textOffset);
                }
            }



        }


        private System.Drawing.Point SkeletonPointToScreen(SkeletonPoint skelpoint)
        {
            // Convert point to depth space.  
            // We are not using depth directly, but we do want the points in our 640x480 output resolution.
            DepthImagePoint depthPoint = this.sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return new System.Drawing.Point(depthPoint.X, depthPoint.Y);
        }

        private void EraseBackground(Bitmap bmp, byte depth)
        {
            if (depth <= 5)
            {
                return;
            }
            short[] column = new short[640];
            short[] row = new short[480];
            System.Drawing.Imaging.BitmapData bmpData;
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
            int BmpStride = bmpData.Stride;
            int step = BmpStride / bmp.Width;
            try
            {
                unsafe
                {
                    byte* pbmp = (byte*)bmpData.Scan0;
                    for (int y = 0; y < 480; y++)
                    {
                        for (int x = 0; x < 640; x++)
                        {
                            byte td = pbmp[x * step + y * BmpStride];

                            if (td >= depth || td == 0)
                            {
                                pbmp[x * step + y * BmpStride] = 255;
                                pbmp[x * step + 1 + y * BmpStride] = 255;
                                pbmp[x * step + 2 + y * BmpStride] = 255;
                            }
                            else
                            {
                                column[x]++;
                                row[y]++;
                            }
                        }
                    }
                }
            }
            catch (Exception e) { Console.WriteLine(e); }
            finally
            {
                bmp.UnlockBits(bmpData);
            }
        }

        private static float tempValueForAngle = (float)(1100.0 / 480 * AngleRotateTan);
        private float CalculatedValueForAngle = 0.83f;
        private void TransformDepth(ref short initialDepth, int y, double angleTan)
        {
            if (initialDepth == 0)
            {
                return;
            }


            short newdepth = Convert.ToInt16(initialDepth + (float)y * CalculatedValueForAngle);
            initialDepth = newdepth;

        }
        Image<Gray, Byte> binaryImg;
        Image<Gray, Byte> grayImg;
        int begin = 50;
        int end = 80;
        int minLength = 90;
        private bool Intersect = false;
        int minSize = 1000;
        Point RightHandCenter = new Point();
        Point LeftHandCenter = new Point();
        private unsafe void FindHandPart(ref Image<Bgra, Byte> image)
        {
            Image<Gray, byte> gray_image = image.Convert<Gray, byte>();
            grayImg = gray_image;
            binaryImg = gray_image.ThresholdBinaryInv(new Gray(200), new Gray(255));
            //Find contours with no holes try CV_RETR_EXTERNAL to find holes
            IntPtr Dyncontour = new IntPtr();//存放检测到的图像块的首地址

            IntPtr Dynstorage = CvInvoke.cvCreateMemStorage(0);
            int n = CvInvoke.cvFindContours(binaryImg.Ptr, Dynstorage, ref Dyncontour, sizeof(MCvContour),
                Emgu.CV.CvEnum.RETR_TYPE.CV_RETR_EXTERNAL, Emgu.CV.CvEnum.CHAIN_APPROX_METHOD.CV_CHAIN_APPROX_SIMPLE, new System.Drawing.Point(0, 0));
            Seq<System.Drawing.Point> DyncontourTemp1 = new Seq<System.Drawing.Point>(Dyncontour, null);//方便对IntPtr类型进行操作
            Seq<System.Drawing.Point> DyncontourTemp = DyncontourTemp1;
            List<MCvBox2D> rectList = new List<MCvBox2D>();
            for (; DyncontourTemp != null && DyncontourTemp.Ptr.ToInt32() != 0; DyncontourTemp = DyncontourTemp.HNext)
            {
                //iterate contours
                if (DyncontourTemp.GetMinAreaRect().GetTrueArea() < minSize)
                {
                    continue;
                }
                CvInvoke.cvDrawContours(image, DyncontourTemp, new MCvScalar(255, 0, 0), new MCvScalar(0, 255, 0), 10, 1, Emgu.CV.CvEnum.LINE_TYPE.FOUR_CONNECTED, new System.Drawing.Point(0, 0));
                PointF[] rect1 = DyncontourTemp.GetMinAreaRect().GetVertices();
                
                rectList.Add(DyncontourTemp.GetMinAreaRect());
                var pointfSeq =
                               from p in rect1
                               select new System.Drawing.Point((int)p.X, (int)p.Y);
                System.Drawing.Point[] points = pointfSeq.ToArray();
                DrawPoly(points, image, new MCvScalar(255, 0, 0));
            }
            rectList = rectList.OrderByDescending(x=>x.GetTrueArea()).ToList();
            MCvBox2D rightHand;
            MCvBox2D leftHand;
           
            System.Drawing.Point[] SplittedLeftHand;
            Font textFont = new Font(FontFamily.Families[0], 20);
            // count hands number
            using (Graphics g = Graphics.FromImage(image.Bitmap))
            {
                if (rectList.Count() >= 2)//two hands
                {
                    if (rectList[0].center.X > rectList[1].center.X)
                    {
                        rightHand = rectList[0];
                        leftHand = rectList[1];
                    }
                    else
                    {
                        rightHand = rectList[1];
                        leftHand = rectList[0];
                    }
                    if (rightHand.MinAreaRect().IsCloseTo(leftHand.MinAreaRect(), 5))
                    {
                        Intersect = true;
                    }
                    else
                    {
                        Intersect = false;
                    }

                    //right hand
                    SplitAndDrawHand(rightHand, image, HandEnum.Right);
                    //left hand
                    SplitAndDrawHand(leftHand, image, HandEnum.Left);
                    g.DrawString("left and right", textFont, Brushes.Red,
                      0, 20);
                    DrawHandPosition(image.Bitmap, LeftHandCenter, Brushes.Yellow);


                }
                else if (rectList.Count() == 1) // one rectangle
                {
                    string text = "";


                    if (Intersect)
                    {
                        text = "Two hands";
                        SplitAndDrawHand(rectList[0], image, HandEnum.Both);
                    }
                    else
                    {
                        text = "right";
                        SplitAndDrawHand(rectList[0], image, HandEnum.Right);
                    }
                    g.DrawString(text, textFont, Brushes.Red,
                        0, 20);

                }
            }
            DrawHandPosition(image.Bitmap, RightHandCenter, Brushes.Yellow);
       
        }
        enum HandEnum
        {
            Right,Left,Both
        }

        private Point GetCenterPoint(Point[] points)
        {
            try
            {
                if (points.Length <= 0)
                {
                    return Point.Empty;
                }
                int X = (int)points.Average((x => x.X));
                int Y = (int)points.Average((x => x.Y));
                return new Point(X, Y);
            }
            catch (Exception)
            {

                return Point.Empty;
            }
            
        }

        private void SplitAndDrawHand(MCvBox2D rect, Image<Bgra, Byte> image, HandEnum handEnum)
        {
            System.Drawing.Point[] SplittedHand = SplitHand(rect, handEnum);
            DrawPoly(SplittedHand, image, new MCvScalar(0, 0, 255));
            Point center = GetCenterPoint(SplittedHand);
            if (center == Point.Empty)
            {
                center = rect.center.ToPoint();
            }
            if (handEnum == HandEnum.Right)
            {
                RightHandCenter = center;
            }
            if (handEnum == HandEnum.Left)
            {
                LeftHandCenter = center;
            }
            if (handEnum == HandEnum.Both)
            {
                RightHandCenter = center;
            }
        }

        private void DrawPoly(System.Drawing.Point[] points, Image<Bgra, Byte> image, MCvScalar color)
        {
            return;
            if (points == null || points.Length<=0)
            {
                return;
            }
            for (int j = 0; j < points.Length; j++)
            {
                CvInvoke.cvLine(image, points[j], points[(j + 1) % points.Length], color, 2, Emgu.CV.CvEnum.LINE_TYPE.EIGHT_CONNECTED, 0);
            }
        }

        private System.Drawing.Point[] SplitHand(MCvBox2D rect, HandEnum handEnum)
        {
            if (handEnum == HandEnum.Both)
            {
                return null;
            }
            PointF[] pl = rect.GetVertices();
            Point[] splittedHands = new Point[4];
            //find angle of long edge
            PointF startP = pl[1];
            PointF shortP = pl[0];
            PointF longP = pl[2];
            PointF ap1 = new PointF();
            PointF ap2 = new PointF();

            if (pl[0].DistanceTo(startP) > pl[2].DistanceTo(startP))
            {
                shortP = pl[2];
                longP = pl[0];
            }

            float longDis = longP.DistanceTo(startP);
            if (longDis < minLength)
            {
                return null;
            }
            float shortDis = shortP.DistanceTo(startP);
            // x and long edge slope 
            float longslope = Math.Abs(longP.X - startP.X) / longDis;
            float min = 9999;
            float max = 0;

            // > 45
            if (longslope < 0.707)//vert
            {
                pl = pl.OrderBy((x => x.Y)).ToArray();
                startP = pl[0];
                shortP = pl[1];
                longP = pl[2];
                for (int y = begin; y < Convert.ToInt32(Math.Abs(longP.Y - startP.Y)) && Math.Abs(y) < end; y++)
                {
                    PointF p1 = InterPolateP(startP, longP, y / Math.Abs(longP.Y - startP.Y));
                    PointF p2 = new PointF(p1.X + shortP.X - startP.X, p1.Y + shortP.Y - startP.Y);
                    float dis = GetHandWidthBetween(p1, p2);
                    if (dis < min)
                    {
                        min = dis;
                        ap1 = p1;
                        ap2 = p2;
                    }
                }
            }
            else // horizontal 
            {
                if (handEnum == HandEnum.Right)
                {
                   pl = pl.OrderBy((x => x.X)).ToArray();
                  
                }
                else if (handEnum == HandEnum.Left)
                {
                   pl = pl.OrderByDescending((x => x.X)).ToArray();
                }
                startP = pl[0];
                shortP = pl[1];
                longP = pl[2];
                for (int X = begin; X < Convert.ToInt32(Math.Abs(longP.X - startP.X)) && Math.Abs(X) < end; X++)
                {
                    PointF p1 = InterPolateP(startP, longP, X / Math.Abs(longP.X - startP.X));
                    PointF p2 = new PointF(p1.X + shortP.X - startP.X, p1.Y + shortP.Y - startP.Y);
                    float dis = GetHandWidthBetween(p1, p2);
                    if (dis < min)
                    {
                        min = dis;
                        ap1 = p1;
                        ap2 = p2;
                    }
                }
            }
            if (ap1 == null || ap1 == PointF.Empty)
            {
                return null;
            }
            splittedHands[0] = startP.ToPoint();
            splittedHands[1] = ap1.ToPoint();
            splittedHands[2] = ap2.ToPoint();
            splittedHands[3] = shortP.ToPoint();
            return splittedHands;
        }

        private PointF InterPolateP(PointF p1, PointF p2, float disToP1)
        {
            float x = (p2.X - p1.X) * Math.Abs(disToP1) + p1.X;
            float y = (p2.Y - p1.Y) * Math.Abs(disToP1) + p1.Y;
            return new PointF(x, y);
        }

        private float GetHandWidthBetween(PointF p1, PointF p2)
        {
            float slope = Math.Abs(p2.X - p1.X) / p2.DistanceTo(p1);
            PointF p3 = new PointF();
            PointF p4 = new PointF();
            if (slope < 0.707)//vert
            {

                for (int Y = 0; Y < Math.Abs(p2.Y - p1.Y); Y++)
                {
                    p3 = InterPolateP(p1, p2, Y / (p2.Y - p1.Y));
                    if (IsHand(p3)) break;

                }
                for (int Y = 0; Y < Math.Abs(p2.Y - p1.Y); Y++)
                {
                    p4 = InterPolateP(p2, p1, Y / (p2.Y - p1.Y));
                    if (IsHand(p4)) break;

                }
                return p3.DistanceTo(p4);
            }
            else//hori
            {
                for (int x = 0; x < Math.Abs(p2.X - p1.X); x++)
                {
                    p3 = InterPolateP(p1, p2, x / (p2.X - p1.X));
                    if (IsHand(p3)) break;

                }
                for (int x = 0; x < Math.Abs(p2.X - p1.X); x++)
                {
                    p4 = InterPolateP(p2, p1, x / (p2.X - p1.X));
                    if (IsHand(p4)) break;

                }
                return p3.DistanceTo(p4);
            }
        }

        private bool IsHand(PointF p)
        {
            try
            {
                if (grayImg[p.ToPoint()].Intensity < 200)
                {
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                return false;
            }
        }


        private void DrawEdge(Bitmap bmp, Bitmap edge)
        {
            System.Drawing.Imaging.BitmapData bmpData;
            System.Drawing.Imaging.BitmapData edgeData;
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
            edgeData = edge.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, edge.PixelFormat);
            int BmpStride = bmpData.Stride;
            int EdgeStride = edgeData.Stride;
            int step = BmpStride / bmp.Width;
            try
            {
                unsafe
                {
                    byte* pbmp = (byte*)bmpData.Scan0;
                    byte* pedge = (byte*)edgeData.Scan0;
                    for (int y = 0; y < 480; y++)
                    {
                        for (int x = 0; x < 640; x++)
                        {

                            if (pedge[x + y * EdgeStride] == 255)
                            {
                                pbmp[x * step + y * BmpStride] = 0;
                                pbmp[x * step + 1 + y * BmpStride] = 0;
                                pbmp[x * step + 2 + y * BmpStride] = 255;
                            }
                        }
                    }
                }

            }
            catch (Exception e) { Console.WriteLine(e); }
            finally
            {
                bmp.UnlockBits(bmpData);
                edge.UnlockBits(edgeData);
            }
        }

        private void UpdateImage(WriteableBitmap wbmp, Bitmap bmp)
        {

            lock (bmp)
            {
                Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                System.Drawing.Imaging.BitmapData bmpData =
                    bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    bmp.PixelFormat);

                try
                {
                    wbmp.Lock();

                    wbmp.WritePixels(
                      new System.Windows.Int32Rect(0, 0, wbmp.PixelWidth, wbmp.PixelHeight),
                      bmpData.Scan0,
                      bmpData.Width * bmpData.Height * 4,
                      bmpData.Stride);
                    wbmp.Unlock();


                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }
            }


        }

        public override void Shutdown()
        {
            if (null != this.sensor)
            {
                this.sensor.Stop();
            }
        }

        public override void Start()
        {
            // Start the sensor!
            try
            {
                this.sensor.Start();
            }
            catch (Exception)
            {
                this.sensor = null;
            }
        }



    }
}